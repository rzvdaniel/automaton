namespace Automaton.Cli.Commands;

/// <summary>Commits a staged pull refresh and restores changed files if any replacement fails.</summary>
internal static class PullFileCommitter
{
    internal static string CreateStagingDirectory(string targetDirectory)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var parent = Directory.GetParent(fullTargetDirectory)?.FullName
            ?? throw new InvalidOperationException("The pull target must have a parent directory.");
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(
            parent, $".{Path.GetFileName(fullTargetDirectory)}.pulling-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        return stagingDirectory;
    }

    internal static void CommitProject(string stagingDirectory, string targetDirectory, bool targetExisted)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        if (!targetExisted)
        {
            Directory.Move(stagingDirectory, fullTargetDirectory);
            return;
        }
        if (!Directory.Exists(fullTargetDirectory))
        {
            throw new InvalidOperationException("The pull target changed before commit.");
        }
        if (Directory.EnumerateFileSystemEntries(fullTargetDirectory).Any())
        {
            throw new InvalidOperationException("The pull target must remain empty until commit.");
        }
        Directory.Delete(fullTargetDirectory);
        Directory.Move(stagingDirectory, fullTargetDirectory);
    }

    internal static async Task StageAndCommitRefreshAsync(
        string targetDirectory,
        IReadOnlyList<PullCommand.SourceDownload> downloads,
        Func<string, Task<string>> readFile,
        bool nonInteractive)
    {
        var stagingDirectory = CreateStagingDirectory(targetDirectory);
        try
        {
            var stagedDownloads = downloads.Select(download => download with
            {
                LocalPath = ResolveTargetPath(
                    stagingDirectory,
                    Path.GetRelativePath(targetDirectory, download.LocalPath))
            }).ToList();
            await PullCommand.DownloadFilesAsync(stagedDownloads, readFile, nonInteractive);
            Commit(stagingDirectory, targetDirectory, downloads);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    internal static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Remote source contained an empty file path.");
        }
        var normalized = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"Remote source path must be project-relative: {relativePath}");
        }
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, normalized));
        var relative = Path.GetRelativePath(fullTargetDirectory, fullPath);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Remote source path points outside the target directory: {relativePath}");
        }
        return fullPath;
    }

    internal static List<PullCommand.SourceDownload> PrepareDownloads(
        IEnumerable<string> files, string targetDirectory)
    {
        var downloads = files
            .Select(remotePath => new PullCommand.SourceDownload(
                remotePath, ResolveTargetPath(targetDirectory, remotePath)))
            .ToList();
        foreach (var download in downloads) EnsureNoReparsePoints(download.LocalPath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            EnsureNoCaseInsensitiveCollisions(downloads);
        }
        return downloads;
    }

    private static void EnsureNoCaseInsensitiveCollisions(
        IEnumerable<PullCommand.SourceDownload> downloads)
    {
        var collision = downloads
            .GroupBy(download => download.LocalPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Remote source paths collide on this case-insensitive filesystem: {string.Join(", ", collision.Select(download => download.RemotePath))}");
        }
    }

    internal static void EnsureNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            EnsureCurrentPathIsNotReparsePoint(current, path);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal)) break;
            current = parent;
        }
    }

    private static void EnsureCurrentPathIsNotReparsePoint(string current, string requestedPath)
    {
        if (!File.Exists(current) && !Directory.Exists(current)) return;
        var attributes = File.GetAttributes(current);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                $"Remote source path cannot be written through a symbolic link or junction: {requestedPath}");
        }
    }

    internal static void Commit(
        string stagingDirectory,
        string targetDirectory,
        IReadOnlyList<PullCommand.SourceDownload> downloads)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var backupDirectory = stagingDirectory + ".rollback";
        var changedFiles = new List<(string Target, string? Backup)>();
        var preserveBackups = false;
        try
        {
            ReplaceStagedFiles(stagingDirectory, fullTargetDirectory, backupDirectory, downloads, changedFiles);
        }
        catch (Exception commitError)
        {
            var rollbackError = RestoreChangedFiles(changedFiles);
            if (rollbackError == null) throw;
            preserveBackups = true;
            throw new InvalidOperationException(
                $"Pull could not finish and local rollback also failed: {rollbackError.Message}. Recovery copies are in '{backupDirectory}'.",
                commitError);
        }
        finally
        {
            DeleteBackupDirectory(backupDirectory, preserveBackups);
        }
    }

    private static void ReplaceStagedFiles(
        string stagingDirectory,
        string fullTargetDirectory,
        string backupDirectory,
        IEnumerable<PullCommand.SourceDownload> downloads,
        ICollection<(string Target, string? Backup)> changedFiles)
    {
        foreach (var download in downloads)
        {
            var relativePath = Path.GetRelativePath(fullTargetDirectory, download.LocalPath);
            var stagedPath = PullCommand.ResolveTargetPath(stagingDirectory, relativePath);
            EnsureStagedFileExists(stagedPath, download.RemotePath);
            PullCommand.EnsureNoReparsePoints(download.LocalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(download.LocalPath)!);
            var backupPath = BackupExistingFile(backupDirectory, relativePath, download.LocalPath);
            changedFiles.Add((download.LocalPath, backupPath));
            File.Move(stagedPath, download.LocalPath, overwrite: true);
        }
    }

    private static void EnsureStagedFileExists(string stagedPath, string remotePath)
    {
        if (!File.Exists(stagedPath))
        {
            throw new InvalidOperationException($"Staged pull file is missing: {remotePath}");
        }
    }

    private static string? BackupExistingFile(
        string backupDirectory, string relativePath, string localPath)
    {
        if (!File.Exists(localPath)) return null;
        var backupPath = PullCommand.ResolveTargetPath(backupDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(localPath, backupPath);
        return backupPath;
    }

    private static Exception? RestoreChangedFiles(
        IEnumerable<(string Target, string? Backup)> changedFiles)
    {
        Exception? rollbackError = null;
        foreach (var changedFile in changedFiles.Reverse())
        {
            try
            {
                RestoreFile(changedFile);
            }
            catch (Exception exception)
            {
                rollbackError ??= exception;
            }
        }
        return rollbackError;
    }

    private static void RestoreFile((string Target, string? Backup) changedFile)
    {
        if (changedFile.Backup is not null)
        {
            File.Copy(changedFile.Backup, changedFile.Target, overwrite: true);
            return;
        }
        if (File.Exists(changedFile.Target)) File.Delete(changedFile.Target);
    }

    private static void DeleteBackupDirectory(string backupDirectory, bool preserveBackups)
    {
        if (!preserveBackups && Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }
}
