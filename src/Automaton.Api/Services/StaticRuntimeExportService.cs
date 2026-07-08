using System.IO.Compression;
using Automaton.Api.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Exports browser-safe frontend runtime files into the disposable static runtime cache.
/// </summary>
public class StaticRuntimeExportService
{
    private readonly IWebHostEnvironment environment;
    private readonly WebAppConfig config;

    public StaticRuntimeExportService(
        IWebHostEnvironment environment,
        IOptions<WebAppConfig> config)
    {
        this.environment = environment;
        this.config = config.Value;
    }

    public async Task ExportFromDirectoryAsync(Guid appId, string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Static runtime export source directory not found: {sourcePath}");
        }

        var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Select(sourceFile => new StaticRuntimeExportFile(
                Path.GetRelativePath(sourcePath, sourceFile).Replace('\\', '/'),
                async (destinationPath, ct) =>
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    await using var input = File.OpenRead(sourceFile);
                    await using var output = File.Create(destinationPath);
                    await input.CopyToAsync(output, ct);
                }))
            .ToList();

        await ExportAsync(appId, files, cancellationToken);
    }

    public async Task ExportFromDeployPackageAsync(Guid appId, IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("A deployment package file is required for static runtime export.");
        }

        var files = new List<StaticRuntimeExportFile>();
        await using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var normalizedPath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (!normalizedPath.StartsWith("runtime/frontend/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = normalizedPath["runtime/frontend/".Length..];
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var capturedEntry = entry;
            files.Add(new StaticRuntimeExportFile(
                relativePath,
                async (destinationPath, ct) =>
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    await using var input = capturedEntry.Open();
                    await using var output = File.Create(destinationPath);
                    await input.CopyToAsync(output, ct);
                }));
        }

        await ExportAsync(appId, files, cancellationToken);
    }

    public string GetAppStaticRuntimePath(Guid appId)
    {
        return ResolveSafePath(GetAppsRootPath(), appId.ToString());
    }

    private async Task ExportAsync(
        Guid appId,
        IReadOnlyCollection<StaticRuntimeExportFile> files,
        CancellationToken cancellationToken)
    {
        var rootPath = GetRootPath();
        var appsRoot = GetAppsRootPath();
        var stagingRoot = ResolveSafePath(rootPath, ".staging");
        var backupRoot = ResolveSafePath(rootPath, ".backup");
        Directory.CreateDirectory(appsRoot);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(backupRoot);

        var suffix = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var stagingPath = ResolveSafePath(stagingRoot, $"{appId}-{suffix}");
        var finalPath = GetAppStaticRuntimePath(appId);
        var backupPath = ResolveSafePath(backupRoot, $"{appId}-{suffix}");
        var indexWritten = false;
        var backupCreated = false;

        Directory.CreateDirectory(stagingPath);

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ValidateRelativePath(file.RelativePath);
                var destinationPath = ResolveSafePath(stagingPath, file.RelativePath);
                await file.CopyToAsync(destinationPath, cancellationToken);

                if (string.Equals(file.RelativePath, "index.html", StringComparison.OrdinalIgnoreCase))
                {
                    indexWritten = true;
                }
            }

            if (!indexWritten)
            {
                throw new InvalidOperationException("Static runtime export requires runtime/frontend/index.html.");
            }

            if (Directory.Exists(finalPath))
            {
                Directory.Move(finalPath, backupPath);
                backupCreated = true;
            }

            Directory.Move(stagingPath, finalPath);

            if (backupCreated && Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }
        }
        catch
        {
            if (backupCreated && Directory.Exists(backupPath) && !Directory.Exists(finalPath))
            {
                Directory.Move(backupPath, finalPath);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
        }
    }

    private string GetRootPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(config.StaticRuntimeRoot)
            ? "AppData/static-runtime"
            : config.StaticRuntimeRoot;

        var rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);

        Directory.CreateDirectory(rootPath);
        return Path.GetFullPath(rootPath);
    }

    private string GetAppsRootPath() => ResolveSafePath(GetRootPath(), "apps");

    private static void ValidateRelativePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new InvalidOperationException("Static runtime export encountered an empty relative path.");
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment == "." || segment == ".." || segment.StartsWith('.')) ||
            normalizedPath.Contains("../", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedPath) ||
            !WebAppRuntimeFilePolicy.IsAllowedPublicPath(normalizedPath))
        {
            throw new InvalidOperationException($"Runtime file '{relativePath}' is not safe for static runtime hosting.");
        }
    }

    private static string ResolveSafePath(string rootPath, string relativePath)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!fullPath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access to files outside the static runtime root is not allowed.");
        }

        return fullPath;
    }

    private sealed record StaticRuntimeExportFile(
        string RelativePath,
        Func<string, CancellationToken, Task> CopyToAsync);
}
