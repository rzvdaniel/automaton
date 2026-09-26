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
}
