namespace Automaton.Mcp.Tools;

/// <summary>
/// Abstracts file system operations within a single scoped directory
/// (e.g. one app's folder, one flow's folder).
/// Implementations enforce path sandboxing, extension rules, and protected-file policies.
/// </summary>
public interface IScopedFileSystem
{
    /// <summary>Reads a file. Returns null if not found.</summary>
    Task<string?> ReadFileAsync(string relativePath);

    /// <summary>Writes (creates or overwrites) a file, creating parent directories as needed.</summary>
    Task WriteFileAsync(string relativePath, string content);

    /// <summary>
    /// Deletes a file.
    /// Throws <see cref="InvalidOperationException"/> for protected files.
    /// </summary>
    Task DeleteFileAsync(string relativePath);

    /// <summary>
    /// Deletes a folder and all its contents.
    /// Throws <see cref="InvalidOperationException"/> for protected folders.
    /// </summary>
    Task DeleteFolderAsync(string relativePath);

    /// <summary>
    /// Renames or moves a file within the current scope.
    /// </summary>
    Task RenameFileAsync(string fromRelativePath, string toRelativePath, bool overwrite = false);

    /// <summary>
    /// Renames or moves a folder within the current scope.
    /// </summary>
    Task RenameFolderAsync(string fromRelativePath, string toRelativePath);

    /// <summary>Lists all files in the scope.</summary>
    Task<IEnumerable<string>> ListFilesAsync();

    /// <summary>
    /// Searches a file for lines matching a pattern (literal or regex).
    /// Returns each match with surrounding context lines.
    /// </summary>
    Task<IEnumerable<FileSearchMatch>> SearchFileAsync(string relativePath, string pattern, bool useRegex, int contextLines);
}

/// <summary>A single match returned by <see cref="IScopedFileSystem.SearchFileAsync"/>.</summary>
public sealed class FileSearchMatch
{
    public int LineNumber { get; init; }
    public string Match   { get; init; } = "";
    public string Context { get; init; } = "";
}
