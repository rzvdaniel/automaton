namespace Automaton.Core.Models;

/// <summary>
/// Resolves paths against an infrastructure-owned root without allowing the
/// caller to escape that root. Relative paths are the only paths accepted by
/// application contracts and storage APIs.
/// </summary>
public static class ManagedPathBoundary
{
    public static bool IsSafeRelative(string? value, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return allowEmpty;

        var normalized = value.Replace('\\', '/');
        return !normalized.Contains('\0')
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !LooksLikeWindowsRootedPath(normalized)
            && normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .All(segment => segment is not "..");
    }

    public static string ResolveInside(
        string root,
        string relativePath,
        string description,
        bool allowRoot = true)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("A managed root is required.", nameof(root));
        if (!IsSafeRelative(relativePath))
            throw new InvalidOperationException($"{description} must remain inside the managed root.");

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalized = relativePath.Replace('\\', '/');
        var fullPath = Path.GetFullPath(
            Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if ((!allowRoot && string.Equals(fullPath, fullRoot, PathComparison))
            || !IsWithin(fullRoot, fullPath))
        {
            throw new InvalidOperationException($"{description} must remain inside the managed root.");
        }

        EnsureNoReparsePointEscape(fullRoot, fullPath, description);
        return fullPath;
    }

    public static bool IsWithin(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullRoot, fullCandidate, PathComparison)
            || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    public static void EnsureNoReparsePointEscape(string root, string path, string description)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsWithin(fullRoot, fullPath))
            throw new InvalidOperationException($"{description} must remain inside the managed root.");

        EnsureNoReparsePointEscapeCore(fullRoot, fullPath, description);
    }

    private static void EnsureNoReparsePointEscapeCore(string root, string path, string description)
    {
        var current = path;
        while (!string.Equals(current, root, PathComparison))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw CreateReparsePointException(
                        current,
                        $"{description} cannot use a link outside the managed root");
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException($"{description} must remain inside the managed root.");
            if (!IsWithin(root, current))
                throw new InvalidOperationException($"{description} must remain inside the managed root.");
        }
    }

    public static void EnsureNoReparsePoints(string root, string description)
    {
        if (!Directory.Exists(root) && !File.Exists(root))
            return;

        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var attributes = EnsureNoReparsePoint(current, description);

            if (!attributes.HasFlag(FileAttributes.Directory))
                continue;

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                pending.Push(entry);
        }
    }

    public static FileAttributes EnsureNoReparsePoint(string path, string description)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw CreateReparsePointException(
                path,
                $"{description} cannot migrate a link or reparse point");

        return attributes;
    }

    private static InvalidOperationException CreateReparsePointException(
        string path,
        string message)
    {
        string? linkTarget = null;
        try
        {
            var attributes = File.GetAttributes(path);
            FileSystemInfo fileSystemInfo = attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            linkTarget = fileSystemInfo.LinkTarget;
        }
        catch (IOException)
        {
            // The path and migration scope are still useful when the target
            // cannot be read safely.
        }
        catch (UnauthorizedAccessException)
        {
            // The path and migration scope are still useful when the target
            // cannot be read safely.
        }

        var target = string.IsNullOrWhiteSpace(linkTarget)
            ? string.Empty
            : $" (target: '{linkTarget}')";
        return new InvalidOperationException($"{message} at '{path}'{target}.");
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool LooksLikeWindowsRootedPath(string value) =>
        value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':';
}
