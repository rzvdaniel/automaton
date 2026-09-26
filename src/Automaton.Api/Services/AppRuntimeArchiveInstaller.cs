using System.IO.Compression;
using Automaton.Api.Models;

namespace Automaton.Api.Services;

/// <summary>Applies bounded public runtime archives while preserving the previous runtime on failure.</summary>
internal static class AppRuntimeArchiveInstaller
{
    public static async Task<RuntimeArchiveUploadResult> InstallAsync(
        Stream packageStream,
        string runtimePath,
        bool clean,
        CancellationToken cancellationToken,
        long maximumBytes = AppArchiveStorageBounds.MaximumExpandedBytes,
        int maximumEntries = AppArchiveStorageBounds.MaximumEntries)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        AppArchiveStorageBounds.Validate(archive, maximumBytes, maximumEntries);

        var fullRuntimePath = Path.GetFullPath(runtimePath);
        var result = new RuntimeArchiveUploadResult([], [], []);
        var entries = CreateWritePlan(archive, fullRuntimePath, result);
        if (result.Errors.Count > 0)
            return result;

        var projectedFiles = ReadCurrentFiles(fullRuntimePath, clean, maximumBytes, maximumEntries);
        ApplyWritePlan(projectedFiles, entries, maximumBytes, maximumEntries);

        var stagingPath = $"{fullRuntimePath}.staging-{Guid.NewGuid():N}";
        var backupPath = $"{fullRuntimePath}.backup-{Guid.NewGuid():N}";
        Directory.CreateDirectory(stagingPath);

        try
        {
            if (!clean && Directory.Exists(fullRuntimePath))
                CopyCurrentFiles(fullRuntimePath, stagingPath);

            await WriteEntriesAsync(entries, stagingPath, maximumBytes, result, cancellationToken);
            if (result.Errors.Count > 0)
                return RejectPartialUpload(result);

            AppArchiveStorageBounds.ValidateDirectory(stagingPath, maximumBytes, maximumEntries);
            ReplaceRuntimeDirectory(fullRuntimePath, stagingPath, backupPath);
            return result;
        }
        finally
        {
            DeleteDirectoryIfExists(stagingPath);
        }
    }

    private static List<RuntimeArchiveEntry> CreateWritePlan(
        ZipArchive archive,
        string runtimePath,
        RuntimeArchiveUploadResult result)
    {
        var entries = new List<RuntimeArchiveEntry>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (ShouldSkipRuntimeUploadEntry(relativePath))
            {
                result.Skipped.Add(CreateSkippedEntry(relativePath, "Excluded by platform rule", "platform"));
                continue;
            }

            if (!WebAppRuntimeFilePolicy.IsAllowedPublicPath(relativePath))
            {
                result.Skipped.Add(CreateSkippedEntry(relativePath, "Blocked by public runtime file policy", "platform"));
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(runtimePath, relativePath));
            if (!destinationPath.StartsWith(runtimePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(new { path = relativePath, reason = "Path traversal detected — file skipped." });
                continue;
            }

            entries.Add(new RuntimeArchiveEntry(entry, relativePath));
        }

        return entries;
    }

    private static Dictionary<string, long> ReadCurrentFiles(
        string runtimePath,
        bool clean,
        long maximumBytes,
        int maximumEntries)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new Dictionary<string, long>(comparer);
        if (clean || !Directory.Exists(runtimePath))
            return files;

        long totalBytes = 0;
        foreach (var filePath in Directory.EnumerateFiles(runtimePath, "*", SearchOption.AllDirectories))
        {
            if (files.Count >= maximumEntries)
                throw new InvalidDataException($"Runtime output contains more than {maximumEntries} files.");

            var relativePath = Path.GetRelativePath(runtimePath, filePath).Replace('\\', '/');
            var fileLength = new FileInfo(filePath).Length;
            if (fileLength > maximumBytes - totalBytes)
                throw new InvalidDataException($"Runtime output exceeds the {maximumBytes}-byte storage limit.");

            files.Add(relativePath, fileLength);
            totalBytes += fileLength;
        }

        return files;
    }

    private static void ApplyWritePlan(
        Dictionary<string, long> files,
        IReadOnlyCollection<RuntimeArchiveEntry> entries,
        long maximumBytes,
        int maximumEntries)
    {
        long totalBytes = files.Values.Sum();
        foreach (var item in entries)
        {
            if (files.TryGetValue(item.RelativePath, out var replacedLength))
                totalBytes -= replacedLength;
            else if (files.Count >= maximumEntries)
                throw new InvalidDataException($"Runtime output exceeds the {maximumEntries}-file limit.");

            if (item.Entry.Length > maximumBytes - totalBytes)
                throw new InvalidDataException($"Runtime output exceeds the {maximumBytes}-byte storage limit.");

            files[item.RelativePath] = item.Entry.Length;
            totalBytes += item.Entry.Length;
        }
    }

    private static void CopyCurrentFiles(string sourceRoot, string stagingRoot)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationPath = Path.Combine(stagingRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourceFile, destinationPath);
        }
    }

    private static async Task WriteEntriesAsync(
        IEnumerable<RuntimeArchiveEntry> entries,
        string stagingRoot,
        long maximumBytes,
        RuntimeArchiveUploadResult result,
        CancellationToken cancellationToken)
    {
        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(stagingRoot, item.RelativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var input = item.Entry.Open();
                await using var output = new FileStream(
                    destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await AppArchiveStorageBounds.CopyExactlyAsync(
                    input, output, item.Entry.Length, maximumBytes, cancellationToken);
                result.Written.Add(item.RelativePath);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Errors.Add(new { path = item.RelativePath, reason = exception.Message });
            }
        }
    }

    private static void ReplaceRuntimeDirectory(string runtimePath, string stagingPath, string backupPath)
    {
        var movedExistingRuntime = false;
        try
        {
            if (Directory.Exists(runtimePath))
            {
                Directory.Move(runtimePath, backupPath);
                movedExistingRuntime = true;
            }

            Directory.Move(stagingPath, runtimePath);
        }
        catch
        {
            if (movedExistingRuntime && Directory.Exists(backupPath) && !Directory.Exists(runtimePath))
                Directory.Move(backupPath, runtimePath);
            throw;
        }

        if (movedExistingRuntime)
            Directory.Delete(backupPath, recursive: true);
    }

    private static RuntimeArchiveUploadResult RejectPartialUpload(RuntimeArchiveUploadResult result)
    {
        result.Written.Clear();
        result.Errors.Add(new
        {
            path = string.Empty,
            reason = "No runtime files were committed because one or more entries failed."
        });
        return result;
    }

    private static bool ShouldSkipRuntimeUploadEntry(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed record RuntimeArchiveEntry(ZipArchiveEntry Entry, string RelativePath);
}
