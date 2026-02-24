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
    public AppScopedFileSystem(string appRootPath) : base(appRootPath) { }

    public override async Task WriteFileAsync(string relativePath, string content)
    {
        AppFileStorageService.ValidateFileExtensionPublic(relativePath);
        await base.WriteFileAsync(relativePath, content);
    }

    public override Task DeleteFileAsync(string relativePath)
    {
        if (relativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete index.html — it is the main app file.");

        return base.DeleteFileAsync(relativePath);
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
}

