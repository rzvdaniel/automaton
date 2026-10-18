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
        EnsureSafeTree(stagingDirectory);
        var destination = Path.Combine(stagingDirectory, metadataFileName);
        var temporary = Path.Combine(stagingDirectory, $".automaton-metadata-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(contents.AsMemory(), cancellationToken);

            EnsureSafeTree(stagingDirectory);
            File.Move(temporary, destination, overwrite: true);
            EnsureSafeTree(stagingDirectory);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void EnsureSafeTree(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (TryGetLinkTarget(fullRoot, out _))
            throw new UnauthorizedAccessException("Action directories cannot be symbolic links or reparse points.");

        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var target = GetLinkTarget(entry, attributes);
                    EnsureAllowedLink(fullRoot, entry, target);
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    public static void CopyValidatedTree(string source, string destination)
    {
        EnsureSafeTree(source);
        CopyDirectory(source, destination);
    }

    public static bool IsLink(string path) => TryGetLinkTarget(path, out _);

    public static long GetSafeTreeSize(string root)
    {
        EnsureSafeTree(root);
        var total = 0L;
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    total += System.Text.Encoding.UTF8.GetByteCount(GetLinkTarget(entry, attributes));
                }
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    total += new FileInfo(entry).Length;
                }
            }
        }
        return total;
    }

    public static void DeleteTree(string path)
    {
        if (!TryGetAttributes(path, out var attributes)) return;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
            else File.Delete(path);
            return;
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path)) DeleteTree(entry);
        Directory.Delete(path);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var targetPath = Path.Combine(destination, Path.GetFileName(entry));
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = GetLinkTarget(entry, attributes);
                if ((attributes & FileAttributes.Directory) != 0)
                    Directory.CreateSymbolicLink(targetPath, target);
                else
                    File.CreateSymbolicLink(targetPath, target);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectory(entry, targetPath);
            }
            else
            {
                File.Copy(entry, targetPath);
            }
        }
    }

    private static void EnsureAllowedLink(string root, string path, string target)
    {
        var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        if (!relativePath.StartsWith(".venv/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Action output links must stay within the Python virtual environment.");

        var virtualEnvironment = Path.GetFullPath(Path.Combine(root, ".venv"));
        var resolvedTarget = Path.GetFullPath(Path.IsPathRooted(target)
            ? target
            : Path.Combine(Path.GetDirectoryName(path)!, target));
        if (IsInside(virtualEnvironment, resolvedTarget)) return;

        if (IsInterpreterLink(relativePath, target)) return;
        throw new UnauthorizedAccessException("Action output links cannot target files outside the Python virtual environment.");
    }

    private static bool IsInterpreterLink(string relativePath, string target)
    {
        const string binPrefix = ".venv/bin/";
        if (!relativePath.StartsWith(binPrefix, StringComparison.Ordinal)
            || relativePath.AsSpan(binPrefix.Length).Contains('/')) return false;
        var linkName = relativePath[binPrefix.Length..];
        return Path.IsPathRooted(target) && IsPythonInterpreterName(linkName)
            && IsPythonInterpreterName(Path.GetFileName(target));
    }

    private static bool IsPythonInterpreterName(string name)
    {
        if (!name.StartsWith("python", StringComparison.Ordinal)) return false;
        var suffix = name.AsSpan("python".Length);
        if (suffix.IsEmpty) return true;
        var hasDigit = false;
        foreach (var character in suffix)
        {
            if (char.IsAsciiDigit(character)) hasDigit = true;
            else if (character != '.') return false;
        }
        return hasDigit;
    }

    private static bool IsInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string GetLinkTarget(string path, FileAttributes attributes)
    {
        var info = (attributes & FileAttributes.Directory) != 0
            ? (FileSystemInfo)new DirectoryInfo(path)
            : new FileInfo(path);
        return info.LinkTarget ?? throw new UnauthorizedAccessException("Unable to inspect Action output link.");
    }

    private static bool TryGetLinkTarget(string path, out string? target)
    {
        if (!TryGetAttributes(path, out var attributes) || (attributes & FileAttributes.ReparsePoint) == 0)
        {
            target = null;
            return false;
        }
        target = GetLinkTarget(path, attributes);
        return true;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}
