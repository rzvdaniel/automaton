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

    private const int LargeFileThreshold = 100;

    public virtual async Task<string?> ReadFileAsync(string relativePath)
    {
        var full = ResolveSafe(relativePath);
        if (!File.Exists(full)) return null;
        var content = await File.ReadAllTextAsync(full);
        var lineCount = content.Count(c => c == '\n') + 1;
        if (lineCount >= LargeFileThreshold)
            content = $"[File has {lineCount} lines. For targeted edits prefer search_file + edit_file to avoid re-reading.]\n" + content;
        return content;
    }

    public virtual async Task WriteFileAsync(string relativePath, string content)
    {
        var full = ResolveSafe(relativePath);
        var dir  = Path.GetDirectoryName(full)!;
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Strip the large-file hint that read_file prepends — the AI sometimes passes
        // the hint-prefixed content straight back into write_file.
        var cleaned = StripLargeFileHint(content);
        await File.WriteAllTextAsync(full, cleaned);
    }

    private static readonly Regex LargeFileHintRegex =
        new(@"^\[File has \d+ lines\. For targeted edits prefer search_file \+ edit_file to avoid re-reading\.\]\r?\n", RegexOptions.Compiled);

    private static string StripLargeFileHint(string content) =>
        LargeFileHintRegex.Replace(content, string.Empty);

    public virtual Task DeleteFileAsync(string relativePath)
    {
        var full = ResolveSafe(relativePath);
        if (File.Exists(full)) File.Delete(full);
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
