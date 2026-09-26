using System.IO.Compression;

namespace Automaton.Api.Services;

/// <summary>Installs a bounded deployment package source snapshot without losing the previous source on failure.</summary>
internal static class AppSourceArchiveInstaller
{
    public static async Task InstallAsync(Stream packageStream, string sourcePath, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        AppArchiveStorageBounds.Validate(archive);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var parentPath = Path.GetDirectoryName(fullSourcePath)
            ?? throw new InvalidOperationException("App source path must have a parent directory.");
        Directory.CreateDirectory(parentPath);

        var token = Guid.NewGuid().ToString("N");
        var stagingPath = $"{fullSourcePath}.staging-{token}";
        var backupPath = $"{fullSourcePath}.backup-{token}";
        Directory.CreateDirectory(stagingPath);

        try
        {
            await ExtractSourceEntriesAsync(archive, stagingPath, cancellationToken);
            ReplaceSourceDirectory(fullSourcePath, stagingPath, backupPath);
        }
        finally
        {
            DeleteDirectoryIfExists(stagingPath);
        }
    }

    private static async Task ExtractSourceEntriesAsync(
        ZipArchive archive,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetSourceRelativePath(entry, out var relativePath))
                continue;

            var destinationPath = ResolveInside(stagingPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await AppArchiveStorageBounds.CopyExactlyAsync(
                input,
                output,
                entry.Length,
                AppArchiveStorageBounds.MaximumExpandedBytes,
                cancellationToken);
        }
    }

    private static bool TryGetSourceRelativePath(ZipArchiveEntry entry, out string relativePath)
    {
        var normalizedPath = entry.FullName.Replace('\\', '/').TrimStart('/');
        if (!normalizedPath.StartsWith("source/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = normalizedPath["source/".Length..];
        return !string.IsNullOrWhiteSpace(relativePath) && !string.IsNullOrEmpty(entry.Name);
    }

    private static string ResolveInside(string rootPath, string relativePath)
    {
        var destinationPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var fullRoot = Path.GetFullPath(rootPath);
        if (!destinationPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Deploy package source entry '{relativePath}' escapes the app source directory.");
        return destinationPath;
    }

    private static void ReplaceSourceDirectory(string sourcePath, string stagingPath, string backupPath)
    {
        var movedExistingSource = false;
        try
        {
            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, backupPath);
                movedExistingSource = true;
            }

            Directory.Move(stagingPath, sourcePath);
        }
        catch
        {
            RestoreExistingSource(sourcePath, backupPath, movedExistingSource);
            throw;
        }

        if (movedExistingSource)
            Directory.Delete(backupPath, recursive: true);
    }

    private static void RestoreExistingSource(string sourcePath, string backupPath, bool movedExistingSource)
    {
        if (movedExistingSource && Directory.Exists(backupPath) && !Directory.Exists(sourcePath))
            Directory.Move(backupPath, sourcePath);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
