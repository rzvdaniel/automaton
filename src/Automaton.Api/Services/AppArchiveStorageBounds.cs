using System.IO.Compression;

namespace Automaton.Api.Services;

internal static class AppArchiveStorageBounds
{
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
}
