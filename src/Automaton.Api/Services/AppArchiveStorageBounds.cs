using System.IO.Compression;

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
        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
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
