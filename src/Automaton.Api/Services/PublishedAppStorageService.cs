using System.Text.Json;
using Automaton.Api.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

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
    private readonly IWebHostEnvironment environment;
    private readonly AppFileStorageService fileStorageService;
    private readonly PublishedAppsConfig config;

    public PublishedAppStorageService(
        IWebHostEnvironment environment,
        AppFileStorageService fileStorageService,
        IOptions<PublishedAppsConfig> config)
    {
        this.environment = environment;
        this.fileStorageService = fileStorageService;
        this.config = config.Value;
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
        return Task.CompletedTask;
    }

    public string BuildPublicUrl(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        return BuildPublicAppUrl(config.PublicBaseUrl, slug);
    }

    public Task<byte[]?> ReadPublishedFileBytesAsync(string slug, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var rootPath = GetRootPath();
        var appPath = ResolveSafePath(rootPath, slug.Trim());
        var filePath = ResolveSafePath(appPath, relativePath);
        return File.Exists(filePath)
            ? File.ReadAllBytesAsync(filePath)
            : Task.FromResult<byte[]?>(null);
    }

    internal static string BuildPublicAppUrl(string? publicBaseUrl, string slug)
    {
        var normalizedBaseUrl = publicBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        var normalizedSlug = Uri.EscapeDataString(slug.Trim());
        var pathPrefix = ShouldNamespaceAppsUnderPath(normalizedBaseUrl) ? "/apps" : string.Empty;
        return $"{normalizedBaseUrl}{pathPrefix}/{normalizedSlug}/";
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
        ValidatePortableIndexHtml(indexHtml);
        await File.WriteAllTextAsync(indexPath, InjectRuntimeScripts(indexHtml), cancellationToken);

        var configPath = Path.Combine(stagingPath, "automaton-config.js");
        await File.WriteAllTextAsync(configPath, CreateConfigScript(metadata), cancellationToken);

        var manifestPath = Path.Combine(stagingPath, "manifest.webmanifest");
        if (!File.Exists(manifestPath))
        {
            await File.WriteAllTextAsync(manifestPath, CreateDefaultManifest(metadata), cancellationToken);
        }
        else
        {
            await ValidatePortableManifestAsync(manifestPath, cancellationToken);
        }
    }

    private static string InjectRuntimeScripts(string html)
    {
        const string runtimeScripts = """
    <script src="./automaton-config.js"></script>
""";

        var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headCloseIndex >= 0)
        {
            return html.Insert(headCloseIndex, runtimeScripts);
        }

        return runtimeScripts + html;
    }

    private string CreateConfigScript(PublishedAppPackageMetadata metadata)
    {
        var payload = new
        {
            appId = metadata.AppId,
            name = metadata.Name,
            version = metadata.Version,
            apiBaseUrl = config.ApiBaseUrl.TrimEnd('/'),
            backendApiPath = $"/api/web-apps/{Uri.EscapeDataString(metadata.Slug)}"
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

    private static void ValidatePortableIndexHtml(string html)
    {
        if (ContainsAutomatonRuntimeReference(html))
        {
            throw new InvalidOperationException(
                "Published app index.html must not include Automaton runtime scripts. " +
                "Automaton injects automaton-config.js at publish time.");
        }

        var invalidReferences = EnumerateHtmlReferences(html)
            .Where(reference => IsRootRelativeLocalReference(reference.Value))
            .Select(reference => $"{reference.Attribute}=\"{reference.Value}\"")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (invalidReferences.Count > 0)
        {
            throw new InvalidOperationException(
                "Published app index.html contains root-relative local URLs. " +
                "Published apps are hosted under a slug path such as apps.automaton.monster/{slug}/ or /apps/{slug}/, so app-local assets and navigation must use relative URLs. " +
                "For Vite, set base: './' in vite.config.ts. Offending references: " +
                string.Join(", ", invalidReferences));
        }
    }

    private static bool ContainsAutomatonRuntimeReference(string html) =>
        html.Contains("automaton-config.js", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("__automaton_api__", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("__AUTOMATON_APP__", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<HtmlReference> EnumerateHtmlReferences(string html)
    {
        for (var i = 0; i < html.Length; i++)
        {
            var attribute = TryReadReferenceAttribute(html, i);
            if (attribute is null)
            {
                continue;
            }

            var valueStart = SkipWhitespace(html, i + attribute.Length);
            if (valueStart >= html.Length || html[valueStart] != '=')
            {
                continue;
            }

            valueStart = SkipWhitespace(html, valueStart + 1);
            if (valueStart >= html.Length)
            {
                continue;
            }

            var quote = html[valueStart];
            if (quote is '"' or '\'')
            {
                var valueEnd = html.IndexOf(quote, valueStart + 1);
                if (valueEnd < 0)
                {
                    continue;
                }

                yield return new HtmlReference(attribute, html[(valueStart + 1)..valueEnd].Trim());
                i = valueEnd;
                continue;
            }

            var unquotedEnd = valueStart;
            while (unquotedEnd < html.Length &&
                !char.IsWhiteSpace(html[unquotedEnd]) &&
                html[unquotedEnd] != '>')
            {
                unquotedEnd++;
            }

            if (unquotedEnd > valueStart)
            {
                yield return new HtmlReference(attribute, html[valueStart..unquotedEnd].Trim());
                i = unquotedEnd;
            }
        }
    }

    private static string? TryReadReferenceAttribute(string html, int index)
    {
        if (MatchesAttributeName(html, index, "src"))
        {
            return "src";
        }

        return MatchesAttributeName(html, index, "href") ? "href" : null;
    }

    private static bool MatchesAttributeName(string html, int index, string attribute)
    {
        if (index > 0 && IsHtmlNameCharacter(html[index - 1]))
        {
            return false;
        }

        if (index + attribute.Length > html.Length ||
            !html.AsSpan(index, attribute.Length).Equals(attribute.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var nextIndex = index + attribute.Length;
        return nextIndex >= html.Length || !IsHtmlNameCharacter(html[nextIndex]);
    }

    private static bool IsHtmlNameCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '-' or '_' or ':';

    private static int SkipWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsRootRelativeLocalReference(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) &&
            !trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    private static async Task ValidatePortableManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        using var manifest = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (manifest.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Published app manifest.webmanifest must be a JSON object.");
        }

        var invalidReferences = new List<string>();
        AddInvalidManifestProperty(manifest.RootElement, "start_url", invalidReferences);
        AddInvalidManifestProperty(manifest.RootElement, "scope", invalidReferences);

        if (manifest.RootElement.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
        {
            foreach (var icon in icons.EnumerateArray())
            {
                AddInvalidManifestProperty(icon, "src", invalidReferences);
            }
        }

        if (invalidReferences.Count > 0)
        {
            throw new InvalidOperationException(
                "Published app manifest.webmanifest contains root-relative URLs. " +
                "Use relative URLs such as './' and './icons/icon-192.png'. Offending references: " +
                string.Join(", ", invalidReferences.Distinct(StringComparer.OrdinalIgnoreCase).Take(10)));
        }
    }

    private static void AddInvalidManifestProperty(
        JsonElement element,
        string propertyName,
        ICollection<string> invalidReferences)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var value = property.GetString() ?? string.Empty;
        if (IsRootRelativeLocalReference(value))
        {
            invalidReferences.Add($"{propertyName}: {value}");
        }
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

    private readonly record struct HtmlReference(string Attribute, string Value);

    private static bool ShouldNamespaceAppsUnderPath(string publicBaseUrl)
    {
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            return true;
        }

        return !uri.Host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase);
    }
}
