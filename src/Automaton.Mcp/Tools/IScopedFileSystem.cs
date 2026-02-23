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

    /// <summary>Lists all files in the scope.</summary>
    Task<IEnumerable<string>> ListFilesAsync();
}
