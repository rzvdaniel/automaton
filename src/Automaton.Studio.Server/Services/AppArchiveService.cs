using Automaton.Studio.Server.Models;
using Serilog;
using System.IO.Compression;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class AppArchiveService
{
    private readonly AppsService appsService;
    private readonly AppFileStorageService fileStorageService;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;

    public AppArchiveService(
        AppsService appsService,
        AppFileStorageService fileStorageService,
        UserContextService userContextService)
    {
        this.appsService = appsService;
        this.fileStorageService = fileStorageService;
        this.userContextService = userContextService;
        logger = Log.ForContext<AppArchiveService>();
    }

    public async Task<AppArchiveExportResult?> ExportAsync(Guid appId, CancellationToken cancellationToken)
    {
        var app = appsService.Get(appId);
        if (app is null)
        {
            return null;
        }

        var ownerId = app.UserId ?? userContextService.GetUserId();
        var sourcePath = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        var archiveFileName = $"{SanitizeArchiveFileName(app.Name)}.zip";

        await using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteManifestAsync(archive, app, cancellationToken);

            if (Directory.Exists(sourcePath))
            {
                foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = Path.GetRelativePath(sourcePath, sourceFile).Replace('\\', '/');
                    if (ShouldSkipExportFile(relativePath))
                    {
                        continue;
                    }

                    var archiveEntry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                    await using var entryStream = archiveEntry.Open();
                    await using var fileStream = System.IO.File.OpenRead(sourceFile);
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }
            }
        }

        return new AppArchiveExportResult
        {
            Content = memoryStream.ToArray(),
            FileName = archiveFileName,
        };
    }

    public async Task<AppArchiveImportResult> ImportAsync(Stream archiveStream, string fileName, CancellationToken cancellationToken)
    {
        Guid? createdAppId = null;

        try
        {
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

            var manifest = await ReadArchiveManifestAsync(archive, cancellationToken);
            var appName = GetImportedAppName(manifest, fileName);

            createdAppId = await appsService.Create(new NewApp { Name = appName });

            var userId = userContextService.GetUserId();
            var sourcePath = fileStorageService.GetAppSourceDirectoryPath(userId, createdAppId.Value);
            var written = await ExtractImportedArchiveAsync(archive, sourcePath, cancellationToken);

            var importedApp = appsService.Get(createdAppId.Value)
                ?? throw new InvalidOperationException("Imported app could not be loaded after creation.");

            importedApp.Name = appName;
            importedApp.Summary = manifest?.Summary ?? string.Empty;
            importedApp.Description = manifest?.Description ?? string.Empty;
            importedApp.RequiresAuthentication = manifest?.RequiresAuthentication ?? importedApp.RequiresAuthentication;
            importedApp.Category = manifest?.Category;
            importedApp.Tags = manifest?.Tags;
            importedApp.IsPublished = false;
            importedApp.PublicUrl = null;
            importedApp.PublishedAt = null;
            importedApp.ExecutionRunnerId = null;
            importedApp.ThumbnailUrl = null;

            await appsService.Update(createdAppId.Value, importedApp);

            var createdApp = appsService.Get(createdAppId.Value)
                ?? throw new InvalidOperationException("Imported app could not be loaded after update.");

            logger.Information(
                "Imported app archive into app {AppId}: {Written} files written",
                createdAppId.Value,
                written.Count);

            return new AppArchiveImportResult
            {
                App = createdApp,
                WrittenFileCount = written.Count,
            };
        }
        catch
        {
            if (createdAppId.HasValue)
            {
                await SafeDeleteImportedAppAsync(createdAppId.Value);
            }

            throw;
        }
    }

    private static async Task WriteManifestAsync(ZipArchive archive, AppModel app, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.CreateEntry(AppArchiveFileNames.Manifest, CompressionLevel.Fastest);
        await using var manifestStream = manifestEntry.Open();

        await JsonSerializer.SerializeAsync(
            manifestStream,
            new AppArchiveManifest
            {
                Name = app.Name,
                Summary = app.Summary,
                Description = app.Description,
                RequiresAuthentication = app.RequiresAuthentication,
                Category = app.Category,
                Tags = app.Tags,
                ExportedAtUtc = DateTime.UtcNow,
            },
            cancellationToken: cancellationToken);
    }

    private static string SanitizeArchiveFileName(string appName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(appName.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "app-export" : sanitized;
    }

    private static bool ShouldSkipExportFile(string relativePath)
    {
        return relativePath.Equals(AppArchiveFileNames.AutomatonConfig, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(AppArchiveFileNames.VersionsFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetImportedAppName(AppArchiveManifest? manifest, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(manifest?.Name))
        {
            return manifest.Name.Trim();
        }

        var fileStem = Path.GetFileNameWithoutExtension(fileName)?.Trim();
        return string.IsNullOrWhiteSpace(fileStem) ? "Imported App" : fileStem;
    }

    private static async Task<AppArchiveManifest?> ReadArchiveManifestAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var manifestEntry = archive.GetEntry(AppArchiveFileNames.Manifest);
        if (manifestEntry == null)
        {
            return null;
        }

        await using var manifestStream = manifestEntry.Open();
        return await JsonSerializer.DeserializeAsync<AppArchiveManifest>(manifestStream, cancellationToken: cancellationToken);
    }

    private static async Task<List<string>> ExtractImportedArchiveAsync(ZipArchive archive, string sourcePath, CancellationToken cancellationToken)
    {
        var written = new List<string>();

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (ShouldSkipImportedEntry(relativePath))
            {
                continue;
            }

            AppFileStorageService.ValidateFileExtensionPublic(relativePath);

            var fileExt = Path.GetExtension(relativePath).ToLowerInvariant();
            if (fileExt is ".py" or ".bat"
                && !relativePath.StartsWith(AppArchiveFileNames.BackendFolder, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Python/Batch files must be in backend/. Use 'backend/{Path.GetFileName(relativePath)}'.");
            }

            var destPath = Path.GetFullPath(Path.Combine(sourcePath, relativePath));
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!destPath.StartsWith(fullSourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Path traversal detected for '{relativePath}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            await using var entryStream = entry.Open();
            await using var outStream = System.IO.File.Create(destPath);
            await entryStream.CopyToAsync(outStream, cancellationToken);
            written.Add(relativePath);
        }

        return written;
    }

    private static bool ShouldSkipImportedEntry(string relativePath)
    {
        return relativePath.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(relativePath).StartsWith('.')
            || relativePath.Equals(AppArchiveFileNames.Manifest, StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(AppArchiveFileNames.AutomatonConfig, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(AppArchiveFileNames.VersionsFolder, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SafeDeleteImportedAppAsync(Guid appId)
    {
        try
        {
            await appsService.Remove(appId);
        }
        catch (Exception cleanupEx)
        {
            logger.Warning(cleanupEx, "Failed to clean up imported app {AppId} after error", appId);
        }
    }
}
