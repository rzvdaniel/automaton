using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class ManagedApplicationArtifactSynchronizer
{
    public static void Replace(
        string artifactRoot,
        IReadOnlyList<ApplicationManagedArtifactFile> files,
        Func<string, string> normalizeRelativePath,
        Action<string, string>? moveDirectory = null,
        Action<string>? deleteDirectory = null)
    {
        var managedRoot = Directory.GetParent(artifactRoot)?.FullName
            ?? throw new InvalidOperationException("Managed artifact root must have a parent directory.");
        var stagingRoot = Path.Combine(managedRoot, $".flows-staging-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(managedRoot, $".flows-backup-{Guid.NewGuid():N}");
        var move = moveDirectory ?? Directory.Move;
        var delete = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
        var committed = false;

        Directory.CreateDirectory(managedRoot);
        try
        {
            Directory.CreateDirectory(stagingRoot);
            foreach (var file in files)
            {
                var relativePath = normalizeRelativePath(file.RelativePath);
                var destination = ManagedPathBoundary.ResolveInside(
                    stagingRoot,
                    relativePath,
                    "managed application artifact");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, file.Content);
            }

            if (Directory.Exists(artifactRoot))
                move(artifactRoot, backupRoot);

            move(stagingRoot, artifactRoot);
            committed = true;

            try
            {
                if (Directory.Exists(backupRoot))
                    delete(backupRoot);
            }
            catch (Exception)
            {
                // The new artifact set is already live. Cleanup can be retried
                // without reporting a false synchronization failure.
            }
        }
        catch
        {
            if (!committed)
            {
                TryDeleteDirectory(stagingRoot);
                if (Directory.Exists(backupRoot) && !Directory.Exists(artifactRoot))
                {
                    try
                    {
                        move(backupRoot, artifactRoot);
                    }
                    catch (Exception restoreException)
                    {
                        throw new IOException("Failed to restore the previous managed artifact set.", restoreException);
                    }
                }
            }

            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Preserve the original synchronization failure.
        }
    }
}
