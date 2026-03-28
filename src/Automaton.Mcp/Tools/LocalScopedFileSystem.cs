using System.Text.RegularExpressions;

namespace Automaton.Mcp.Tools;

/// <summary>
/// Generic <see cref="IScopedFileSystem"/> implementation backed by <see cref="System.IO"/>.
/// All operations are sandboxed to <see cref="RootPath"/> — path traversal attempts throw
/// <see cref="UnauthorizedAccessException"/>.
///
/// Subclass this to add context-specific guards (extension whitelists, protected files, etc.)
/// without duplicating the core IO logic.
/// </summary>
public class LocalScopedFileSystem : IScopedFileSystem
{
    /// <summary>The absolute root directory all operations are confined to.</summary>
    protected string RootPath { get; }

    public LocalScopedFileSystem(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    // ── IScopedFileSystem ────────────────────────────────────────────────────

    internal const int LargeFileThreshold = 100;

    public virtual async Task<string?> ReadFileAsync(string relativePath)
    {
        var full = ResolveSafe(relativePath);
        if (!File.Exists(full)) return null;
        return await File.ReadAllTextAsync(full);
    }

    public virtual async Task WriteFileAsync(string relativePath, string content)
    {
        var full = ResolveSafe(relativePath);
        var dir  = Path.GetDirectoryName(full)!;
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(full, content);
    }

    public virtual Task DeleteFileAsync(string relativePath)
    {
        var full = ResolveSafe(relativePath);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public virtual Task RenameFileAsync(string fromRelativePath, string toRelativePath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(fromRelativePath))
            throw new InvalidOperationException("Source file path is required.");
        if (string.IsNullOrWhiteSpace(toRelativePath))
            throw new InvalidOperationException("Destination file path is required.");

        var fromFull = ResolveSafe(fromRelativePath);
        var toFull   = ResolveSafe(toRelativePath);

        if (!File.Exists(fromFull))
            throw new InvalidOperationException($"Source file '{fromRelativePath}' does not exist.");

        if (Directory.Exists(toFull))
            throw new InvalidOperationException($"Destination '{toRelativePath}' is a directory, not a file path.");

        var toDir = Path.GetDirectoryName(toFull);
        if (!string.IsNullOrEmpty(toDir))
            Directory.CreateDirectory(toDir);

        if (File.Exists(toFull))
        {
            if (!overwrite)
                throw new InvalidOperationException($"Destination file '{toRelativePath}' already exists. Pass overwrite=true to replace it.");
            File.Delete(toFull);
        }

        File.Move(fromFull, toFull);
        return Task.CompletedTask;
    }

    public virtual Task RenameFolderAsync(string fromRelativePath, string toRelativePath)
    {
        if (string.IsNullOrWhiteSpace(fromRelativePath))
            throw new InvalidOperationException("Source folder path is required.");
        if (string.IsNullOrWhiteSpace(toRelativePath))
            throw new InvalidOperationException("Destination folder path is required.");

        var fromFull = ResolveSafe(fromRelativePath);
        var toFull   = ResolveSafe(toRelativePath);

        if (!Directory.Exists(fromFull))
            throw new InvalidOperationException($"Source folder '{fromRelativePath}' does not exist.");

        if (File.Exists(toFull))
            throw new InvalidOperationException($"Destination '{toRelativePath}' is a file path, not a folder path.");
        if (Directory.Exists(toFull))
            throw new InvalidOperationException($"Destination folder '{toRelativePath}' already exists.");

        var toParent = Path.GetDirectoryName(toFull);
        if (!string.IsNullOrEmpty(toParent))
            Directory.CreateDirectory(toParent);

        Directory.Move(fromFull, toFull);
        return Task.CompletedTask;
    }

    public virtual Task<IEnumerable<string>> ListFilesAsync()
    {
        if (!Directory.Exists(RootPath))
            return Task.FromResult(Enumerable.Empty<string>());

        var files = Directory
            .GetFiles(RootPath, "*.*", SearchOption.AllDirectories)
            .Select(f =>
            {
                var rel = Path.GetRelativePath(RootPath, f).Replace('\\', '/');
                try
                {
                    var lines = File.ReadLines(f).Count();
                    return $"{rel} ({lines} lines)";
                }
                catch
                {
                    return rel;
                }
            });

        return Task.FromResult(files);
    }

    public virtual async Task<IEnumerable<FileSearchMatch>> SearchFileAsync(
        string relativePath, string pattern, bool useRegex, int contextLines)
    {
        var content = await ReadFileAsync(relativePath);
        if (content is null) return [];

        var lines   = content.Split('\n');
        var matches = new List<FileSearchMatch>();

        Func<string, bool> isMatch = useRegex
            ? line => Regex.IsMatch(line, pattern)
            : line => line.Contains(pattern, StringComparison.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!isMatch(lines[i])) continue;

            var from    = Math.Max(0, i - contextLines);
            var to      = Math.Min(lines.Length - 1, i + contextLines);
            var context = string.Join('\n', lines[from..(to + 1)]);

            matches.Add(new FileSearchMatch
            {
                LineNumber = i + 1,
                Match      = lines[i],
                Context    = context
            });
        }

        return matches;
    }

    // ── Path safety ──────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <see cref="RootPath"/> and
    /// throws <see cref="UnauthorizedAccessException"/> if the result escapes the root.
    /// </summary>
    protected string ResolveSafe(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (!full.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Access to '{relativePath}' is outside the allowed directory.");
        return full;
    }
}
