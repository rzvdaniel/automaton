using System.Text.Json;
using System.Text.RegularExpressions;
using Automaton.Studio.Server.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

public sealed record PublishedAppPackageMetadata(
    Guid AppId,
    string Slug,
    string Name,
    string? Summary,
    string? Description,
    int Version);

/// <summary>
/// Exports deployed app frontend files into the public static app host folder.
/// </summary>
public class PublishedAppStorageService
{
    private static readonly Regex HeadCloseRegex = new("</head>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OldAutomatonScriptRegex = new(
        @"<script\b(?=[^>]*(?:id\s*=\s*[""']__automaton_api__[""']|src\s*=\s*[""'][^""']*(?:automaton-api|automaton-config)\.js[""']))[^>]*>(?:[\s\S]*?</script>)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RootAbsoluteAssetRegex = new(
        @"(?<attr>\b(?:src|href)\s*=\s*[""'])/(?!(?:/|api/|app-content/|app-thumbnails/|app-screenshots/|runtime/))(?<path>[^""'#?]+(?:[?#][^""']*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IWebHostEnvironment environment;
    private readonly AppFileStorageService fileStorageService;
    private readonly PublishedAppsConfig config;
    private readonly ILogger<PublishedAppStorageService> logger;

    public PublishedAppStorageService(
        IWebHostEnvironment environment,
        AppFileStorageService fileStorageService,
        IOptions<PublishedAppsConfig> config,
        ILogger<PublishedAppStorageService> logger)
    {
        this.environment = environment;
        this.fileStorageService = fileStorageService;
        this.config = config.Value;
        this.logger = logger;
    }

    public async Task PublishAsync(
        Guid ownerId,
        Guid appId,
        PublishedAppPackageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = fileStorageService.GetAppRuntimeFrontendDirectoryPath(ownerId, appId);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Published app frontend runtime folder not found: {sourcePath}");
        }

        var indexPath = Path.Combine(sourcePath, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Publish requires a deployed frontend package with index.html.", indexPath);
        }

        var rootPath = GetRootPath();
        var stagingPath = Path.Combine(rootPath, ".staging", $"{metadata.Slug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        var finalPath = Path.Combine(rootPath, metadata.Slug);

        Directory.CreateDirectory(stagingPath);

        try
        {
            CopyPublicFiles(sourcePath, stagingPath);
            await WriteRuntimeFilesAsync(stagingPath, metadata, cancellationToken);

            if (Directory.Exists(finalPath))
            {
                Directory.Delete(finalPath, recursive: true);
            }

            Directory.Move(stagingPath, finalPath);
            logger.LogInformation("Published app {AppId} to static app host slug {Slug}", appId, metadata.Slug);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }

            throw;
        }
    }

    public Task UnpublishAsync(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Task.CompletedTask;
        }

        var rootPath = GetRootPath();
        var finalPath = Path.Combine(rootPath, slug);
        if (!Directory.Exists(finalPath))
        {
            return Task.CompletedTask;
        }

        var disabledRoot = Path.Combine(rootPath, ".disabled");
        Directory.CreateDirectory(disabledRoot);
        var disabledPath = Path.Combine(disabledRoot, $"{slug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        Directory.Move(finalPath, disabledPath);

        logger.LogInformation("Unpublished app slug {Slug} from static app host", slug);
        return Task.CompletedTask;
    }

    public string BuildPublicUrl(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        return $"{config.PublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(slug.Trim())}/";
    }

    private string GetRootPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.RootPath)
            ? "AppData/published-apps"
            : config.RootPath;

        var rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        Directory.CreateDirectory(rootPath);
        return Path.GetFullPath(rootPath);
    }

    private static void CopyPublicFiles(string sourcePath, string stagingPath)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile).Replace('\\', '/');
            if (IsBlockedPath(relativePath) || !WebAppRuntimeFilePolicy.IsAllowedPublicPath(relativePath))
            {
                throw new InvalidOperationException($"Runtime file '{relativePath}' is not safe for public app hosting.");
            }

            var targetPath = ResolveSafePath(stagingPath, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourceFile, targetPath, overwrite: true);
        }
    }

    private async Task WriteRuntimeFilesAsync(
        string stagingPath,
        PublishedAppPackageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(stagingPath, "index.html");
        var indexHtml = await File.ReadAllTextAsync(indexPath, cancellationToken);
        await File.WriteAllTextAsync(indexPath, PrepareIndexHtml(indexHtml), cancellationToken);

        var configPath = Path.Combine(stagingPath, "automaton-config.js");
        await File.WriteAllTextAsync(configPath, CreateConfigScript(metadata), cancellationToken);

        var manifestPath = Path.Combine(stagingPath, "manifest.webmanifest");
        if (!File.Exists(manifestPath))
        {
            await File.WriteAllTextAsync(manifestPath, CreateDefaultManifest(metadata), cancellationToken);
        }
    }

    private static string PrepareIndexHtml(string html)
    {
        var prepared = OldAutomatonScriptRegex.Replace(html, string.Empty);
        prepared = RootAbsoluteAssetRegex.Replace(prepared, "${attr}${path}");

        const string runtimeScripts = """
    <script src="./automaton-config.js"></script>
    <script src="/runtime/automaton-api.js"></script>
""";

        if (HeadCloseRegex.IsMatch(prepared))
        {
            return HeadCloseRegex.Replace(prepared, runtimeScripts + "</head>", 1);
        }

        return runtimeScripts + prepared;
    }

    private string CreateConfigScript(PublishedAppPackageMetadata metadata)
    {
        var payload = new
        {
            slug = metadata.Slug,
            appId = metadata.AppId,
            name = metadata.Name,
            version = metadata.Version,
            apiBaseUrl = config.ApiBaseUrl.TrimEnd('/')
        };

        return $"window.__AUTOMATON_APP__ = {JsonSerializer.Serialize(payload)};{Environment.NewLine}";
    }

    private static string CreateDefaultManifest(PublishedAppPackageMetadata metadata)
    {
        var shortName = metadata.Name.Length <= 12 ? metadata.Name : metadata.Name[..12];
        var manifest = new
        {
            name = metadata.Name,
            short_name = shortName,
            description = metadata.Summary ?? metadata.Description ?? string.Empty,
            start_url = "./",
            scope = "./",
            display = "standalone",
            background_color = "#ffffff",
            theme_color = "#2563eb",
            icons = Array.Empty<object>()
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsBlockedPath(string relativePath)
    {
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Length == 0 ||
            segments.Any(segment => segment == "." || segment == ".." || segment.StartsWith('.')) ||
            relativePath.Contains("../", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath);
    }

    private static string ResolveSafePath(string rootPath, string relativePath)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access to files outside app directory is not allowed.");
        }

        return fullPath;
    }
}
