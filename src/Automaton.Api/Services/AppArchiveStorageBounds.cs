using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Automaton.Api.Services;

internal static class AppArchiveStorageBounds
{
    public const long MaximumUploadBytes = 50L * 1024 * 1024;
    public const long MaximumExpandedBytes = 200L * 1024 * 1024;
    public const int MaximumEntries = 10_000;

    public static void Validate(
        ZipArchive archive,
        long maximumExpandedBytes = MaximumExpandedBytes,
        int maximumEntries = MaximumEntries)
    {
        if (archive.Entries.Count > maximumEntries)
            throw new InvalidDataException($"Archive contains more than {maximumEntries} entries.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (entry.Length > maximumExpandedBytes - expandedBytes)
                throw new InvalidDataException($"Archive expands beyond the {maximumExpandedBytes}-byte storage limit.");
            expandedBytes += entry.Length;
        }
    }

    public static void ValidateDirectory(
        string rootPath,
        long maximumBytes = MaximumExpandedBytes,
        int maximumEntries = MaximumEntries)
    {
        MeasureDirectory(rootPath, maximumBytes, maximumEntries);
    }

    internal static IEnumerable<string> EnumerateRegularFilesWithoutLinks(string rootPath)
    {
        if (!Directory.Exists(rootPath))
            yield break;
        if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
            yield break;

        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    public static void ValidateFileWrite(
        string rootPath,
        string relativePath,
        long replacementBytes,
        long maximumBytes = MaximumExpandedBytes,
        int maximumEntries = MaximumEntries)
    {
        if (replacementBytes < 0 || replacementBytes > maximumBytes)
            throw new InvalidDataException($"File exceeds the {maximumBytes}-byte storage limit.");

        var fullRootPath = Path.GetFullPath(rootPath);
        var fullFilePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        EnsurePathWithinRoot(fullRootPath, fullFilePath);

        var usage = MeasureDirectory(fullRootPath, maximumBytes, maximumEntries, fullFilePath);
        if (!File.Exists(fullFilePath) && usage.FileCount >= maximumEntries)
            throw new InvalidDataException($"Directory contains more than {maximumEntries} files.");

        if (replacementBytes > maximumBytes - usage.TotalBytes)
            throw new InvalidDataException($"Directory exceeds the {maximumBytes}-byte storage limit.");
    }

    public static async Task WriteBoundedTextFileAsync(
        string rootPath,
        string relativePath,
        string content,
        long maximumBytes = MaximumExpandedBytes,
        CancellationToken cancellationToken = default)
    {
        await using var writeLock = await AcquireWriteLockAsync(rootPath, cancellationToken);
        ValidateFileWrite(rootPath, relativePath, Encoding.UTF8.GetByteCount(content), maximumBytes);

        var fullRootPath = Path.GetFullPath(rootPath);
        var fullFilePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        var directoryPath = Path.GetDirectoryName(fullFilePath);
        if (!string.IsNullOrEmpty(directoryPath))
            Directory.CreateDirectory(directoryPath);

        await File.WriteAllTextAsync(fullFilePath, content, cancellationToken);
    }

    public static async Task<FileStream> AcquireWriteLockAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var fullRootPath = Path.GetFullPath(rootPath);
        var parentPath = Path.GetDirectoryName(fullRootPath)
            ?? throw new InvalidOperationException("The bounded storage root must have a parent directory.");
        var rootName = Path.GetFileName(fullRootPath);
        if (string.IsNullOrWhiteSpace(rootName))
            throw new InvalidOperationException("The bounded storage root must be a named directory.");

        Directory.CreateDirectory(parentPath);
        // Keep the lock beside the root so directory swaps preserve the same cross-process lock.
        var lockPath = Path.Combine(parentPath, $".{rootName}.automaton-write.lock");
        var waitStarted = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, useAsync: true);
            }
            catch (IOException) when (waitStarted.Elapsed < TimeSpan.FromMinutes(2))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    public static async Task CopyExactlyAsync(
        Stream input,
        Stream output,
        long expectedBytes,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (expectedBytes < 0 || expectedBytes > maximumBytes)
            throw new InvalidDataException($"Stream exceeds the {maximumBytes}-byte storage limit.");

        var buffer = new byte[81920];
        long copiedBytes = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (read > expectedBytes - copiedBytes)
                throw new InvalidDataException("Stream expanded beyond its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copiedBytes += read;
        }

        if (copiedBytes != expectedBytes)
            throw new InvalidDataException("Stream size did not match its declared size.");
    }

    private static DirectoryUsage MeasureDirectory(
        string rootPath,
        long maximumBytes,
        int maximumEntries,
        string? excludedFilePath = null)
    {
        if (!Directory.Exists(rootPath))
            return new DirectoryUsage(0, 0);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var excludedPath = excludedFilePath is null ? null : Path.GetFullPath(excludedFilePath);
        long totalBytes = 0;
        var fileCount = 0;
        foreach (var filePath in EnumerateRegularFilesWithoutLinks(rootPath))
        {
            var fullFilePath = Path.GetFullPath(filePath);
            if (excludedPath is not null && comparer.Equals(fullFilePath, excludedPath))
                continue;

            if (++fileCount > maximumEntries)
                throw new InvalidDataException($"Directory contains more than {maximumEntries} files.");

            var fileLength = new FileInfo(fullFilePath).Length;
            if (fileLength > maximumBytes - totalBytes)
                throw new InvalidDataException($"Directory exceeds the {maximumBytes}-byte storage limit.");
            totalBytes += fileLength;
        }

        return new DirectoryUsage(totalBytes, fileCount);
    }

    private static void EnsurePathWithinRoot(string rootPath, string filePath)
    {
        var rootPrefix = Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!filePath.StartsWith(rootPrefix, comparison))
            throw new UnauthorizedAccessException("File path escapes the storage directory.");
    }

    private sealed record DirectoryUsage(long TotalBytes, int FileCount);
}
