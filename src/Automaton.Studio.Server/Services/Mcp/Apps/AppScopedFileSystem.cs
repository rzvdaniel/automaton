using Automaton.Mcp.Tools;

namespace Automaton.Studio.Server.Services.Mcp.Apps;

/// <summary>
/// App-scoped file system: extends <see cref="LocalScopedFileSystem"/> with app-specific guards.
/// - Extension whitelist (prevents uploading executable scripts)
/// - index.html deletion protection
/// - Hides internal files (_metadata.json, versions/) from listings
/// </summary>
public class AppScopedFileSystem : LocalScopedFileSystem
{
    private const string MainAppFile = "index.html";

    public AppScopedFileSystem(string appRootPath) : base(appRootPath) { }

    public override async Task WriteFileAsync(string relativePath, string content)
    {
        AppFileStorageService.ValidateFileExtensionPublic(relativePath);
        await base.WriteFileAsync(relativePath, content);
    }

    public override Task DeleteFileAsync(string relativePath)
    {
        if (relativePath.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete index.html — it is the main app file.");

        return base.DeleteFileAsync(relativePath);
    }

    public override Task RenameFileAsync(string fromRelativePath, string toRelativePath, bool overwrite = false)
    {
        if (fromRelativePath.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot rename index.html — it is the main app file.");
        if (toRelativePath.Equals(MainAppFile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot replace index.html via rename. Edit index.html directly instead.");
        if (IsInternalVersionsPath(fromRelativePath) || IsInternalVersionsPath(toRelativePath))
            throw new InvalidOperationException("Cannot rename files inside internal versions storage.");

        AppFileStorageService.ValidateFileExtensionPublic(toRelativePath);
        return base.RenameFileAsync(fromRelativePath, toRelativePath, overwrite);
    }

    public override Task RenameFolderAsync(string fromRelativePath, string toRelativePath)
    {
        if (IsInternalVersionsPath(fromRelativePath) || IsInternalVersionsPath(toRelativePath))
            throw new InvalidOperationException("Cannot rename folders inside internal versions storage.");

        return base.RenameFolderAsync(fromRelativePath, toRelativePath);
    }

    public override Task<IEnumerable<string>> ListFilesAsync()
    {
        if (!Directory.Exists(RootPath))
            return Task.FromResult(Enumerable.Empty<string>());

        var versionsPath = Path.GetFullPath(Path.Combine(RootPath, "versions"));

        var files = Directory
            .GetFiles(RootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_metadata.json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFullPath(f).StartsWith(versionsPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(RootPath, f).Replace('\\', '/'));

        return Task.FromResult(files);
    }

    private static bool IsInternalVersionsPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return normalized.Equals("versions", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("versions/", StringComparison.OrdinalIgnoreCase);
    }
}

