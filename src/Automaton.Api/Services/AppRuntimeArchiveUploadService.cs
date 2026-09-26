using System.IO.Compression;
using Automaton.Api.Constants;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

public sealed class AppRuntimeArchiveUploadService(
    AppsService appsService,
    UserContextService userContextService,
    AppFileStorageService fileStorageService,
    AppVersionService appVersionService,
    ILogger<AppRuntimeArchiveUploadService> logger)
{
    public async Task<RuntimeArchiveUploadResult?> UploadAsync(Guid appId, IFormFile file, bool clean)
    {
        var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(appId);
        if (appInfo is null)
        {
            return null;
        }

        var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
        var runtimePath = fileStorageService.GetAppRuntimeDirectoryPath(ownerId, appId);
        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        AppArchiveStorageBounds.Validate(archive);
        await PrepareRuntimeDirectoryAsync(ownerId, appId, runtimePath, clean);

        var result = await CopyEntriesAsync(archive, runtimePath);

        logger.LogDebug(
            "Zip upload for app {AppId}: {Written} written, {Skipped} skipped, {Errors} errors (clean={Clean})",
            appId,
            result.Written.Count,
            result.Skipped.Count,
            result.Errors.Count,
            clean);

        await appsService.SetLocalDevelopmentMetadataAsync(appId, ownerId, runtimeSource: AppRuntimeSources.CliBuild);
        await TryCreateVersionSnapshotAsync(appId, result.Written.Count);
        return result;
    }

    private async Task PrepareRuntimeDirectoryAsync(Guid ownerId, Guid appId, string runtimePath, bool clean)
    {
        if (clean)
        {
            await fileStorageService.ClearApplicationRuntimeAsync(ownerId, appId);
            return;
        }

        Directory.CreateDirectory(runtimePath);
    }

    private static async Task<RuntimeArchiveUploadResult> CopyEntriesAsync(ZipArchive archive, string runtimePath)
    {
        var result = new RuntimeArchiveUploadResult([], [], []);
        foreach (var entry in archive.Entries)
        {
            await CopyEntryAsync(entry, runtimePath, result);
        }

        return result;
    }

    private static async Task CopyEntryAsync(
        ZipArchiveEntry entry,
        string runtimePath,
        RuntimeArchiveUploadResult result)
    {
        if (string.IsNullOrEmpty(entry.Name))
        {
            return;
        }

        var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
        if (ShouldSkipRuntimeUploadEntry(relativePath))
        {
            result.Skipped.Add(CreateSkippedEntry(relativePath, "Excluded by platform rule", "platform"));
            return;
        }

        if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(relativePath))
        {
            result.Skipped.Add(CreateSkippedEntry(relativePath, "Blocked by public runtime file policy", "platform"));
            return;
        }

        var destinationPath = Path.GetFullPath(Path.Combine(runtimePath, relativePath));
        var fullRuntimePath = Path.GetFullPath(runtimePath);
        if (!destinationPath.StartsWith(fullRuntimePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add(new { path = relativePath, reason = "Path traversal detected — file skipped." });
            return;
        }

        await TryWriteEntryAsync(entry, destinationPath, relativePath, result);
    }

    private static async Task TryWriteEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        string relativePath,
        RuntimeArchiveUploadResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var entryStream = entry.Open();
            await using var outputStream = System.IO.File.Create(destinationPath);
            await AppArchiveStorageBounds.CopyEntryAsync(entryStream, outputStream, entry.Length);
            result.Written.Add(relativePath);
        }
        catch (Exception exception)
        {
            result.Errors.Add(new { path = relativePath, reason = exception.Message });
        }
    }

    private async Task TryCreateVersionSnapshotAsync(Guid appId, int writtenCount)
    {
        if (writtenCount == 0)
        {
            return;
        }

        try
        {
            await appVersionService.CreateSnapshotAsync(
                appId,
                userContextService.GetUserId(),
                AppVersionTrigger.ManualSave);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Version snapshot failed for app {AppId} after zip upload — upload still succeeded", appId);
        }
    }

    private static bool ShouldSkipRuntimeUploadEntry(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = segments.LastOrDefault() ?? string.Empty;
        return IsExcludedRuntimeFileName(fileName) || ContainsExcludedRuntimeDirectory(segments);
    }

    private static bool IsExcludedRuntimeFileName(string fileName) =>
        fileName.StartsWith(".", StringComparison.Ordinal) ||
        fileName.Equals("_metadata.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("automaton.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("automaton.local.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".pem", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExcludedRuntimeDirectory(IEnumerable<string> segments) =>
        segments.Any(segment =>
            segment.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("src", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("source", StringComparison.OrdinalIgnoreCase));

    private static object CreateSkippedEntry(string path, string reason, string ruleSource) => new
    {
        path,
        reason,
        ruleSource
    };
}

public sealed record RuntimeArchiveUploadResult(
    List<string> Written,
    List<object> Skipped,
    List<object> Errors);
