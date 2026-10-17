using System.Text;

namespace Automaton.Runner.Console.Services;

/// <summary>Rejects dependency-container links before Runner-side output handling.</summary>
internal static class AppActionDeploymentOutputGuard
{
    public static async Task WriteMetadataAsync(
        string stagingDirectory,
        string metadataFileName,
        string contents,
        CancellationToken cancellationToken)
    {
        EnsureNoLinks(stagingDirectory);
        var destination = Path.Combine(stagingDirectory, metadataFileName);
        var temporary = Path.Combine(stagingDirectory, $".automaton-metadata-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(contents.AsMemory(), cancellationToken);

            EnsureNoLinks(stagingDirectory);
            File.Move(temporary, destination, overwrite: true);
            EnsureNoLinks(stagingDirectory);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void EnsureNoLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Action deployment output cannot contain symbolic links or reparse points.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }
}
