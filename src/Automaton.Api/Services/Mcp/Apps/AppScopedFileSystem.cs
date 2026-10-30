using Automaton.Mcp.Tools;

namespace Automaton.Api.Services.Mcp.Apps;

/// <summary>
/// App-scoped file system: extends <see cref="LocalScopedFileSystem"/> with app-specific guards.
/// - Extension whitelist (prevents uploading executable scripts)
/// - frontend/index.html deletion protection
/// - Works inside the app source/ folder only; runtime files are not editable through MCP tools
/// </summary>
public class AppScopedFileSystem : LocalScopedFileSystem
{
    private const string MainAppFile = "frontend/index.html";

    public AppScopedFileSystem(string appRootPath) : base(appRootPath) { }

    public override async Task WriteFileAsync(string relativePath, string content)
    {
        AppFileStorageService.ValidateFileExtensionPublic(relativePath);

        // Enforce backend-only placement for executable scripts.
        // Python and Batch files must live in backend/ — never at the root or in frontend folders.
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (ext is ".py" or ".bat")
        {
            var normalized = relativePath.Replace('\\', '/').TrimStart('/');
            if (!normalized.StartsWith("backend/"))
                throw new InvalidOperationException(
                    $"Python and Batch files must be placed in backend/. " +
                    $"Use path 'backend/{Path.GetFileName(relativePath)}' instead of '{relativePath}'.");
        }

        await AppArchiveStorageBounds.WriteBoundedTextFileAsync(RootPath, relativePath, content);
    }

    public override Task DeleteFileAsync(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete frontend/index.html — it is the main app file.");

        return base.DeleteFileAsync(relativePath);
    }

    public override async Task RenameFileAsync(string fromRelativePath, string toRelativePath, bool overwrite = false)
    {
        var fromNormalized = fromRelativePath.Replace('\\', '/').TrimStart('/');
        var toNormalized   = toRelativePath.Replace('\\', '/').TrimStart('/');
        if (fromNormalized.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot rename frontend/index.html — it is the main app file.");
        if (toNormalized.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot replace frontend/index.html via rename. Edit frontend/index.html directly instead.");

        AppFileStorageService.ValidateFileExtensionPublic(toRelativePath);
        await using var writeLock = await AppArchiveStorageBounds.AcquireWriteLockAsync(RootPath);
        await base.RenameFileAsync(fromRelativePath, toRelativePath, overwrite);
    }

    public override async Task RenameFolderAsync(string fromRelativePath, string toRelativePath)
    {
        await using var writeLock = await AppArchiveStorageBounds.AcquireWriteLockAsync(RootPath);
        await base.RenameFolderAsync(fromRelativePath, toRelativePath);
    }

    public override Task<IEnumerable<string>> ListFilesAsync()
    {
        if (!Directory.Exists(RootPath))
            return Task.FromResult(Enumerable.Empty<string>());

        var files = Directory
            .GetFiles(RootPath, "*.*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RootPath, f).Replace('\\', '/'));

        return Task.FromResult(files);
    }
}
