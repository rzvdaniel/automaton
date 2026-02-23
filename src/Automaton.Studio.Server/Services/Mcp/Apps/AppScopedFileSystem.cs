using Automaton.Studio.Server.Services.Mcp.Tools;

namespace Automaton.Studio.Server.Services.Mcp.Apps;

/// <summary>
/// App-scoped file system: sandboxes all operations to /AppData/apps/{userId}/{appId}/.
/// Enforces the extension whitelist and protects index.html from deletion.
/// </summary>
public class AppScopedFileSystem : IScopedFileSystem
{
    private readonly AppFileStorageService _storage;
    private readonly Guid _userId;
    private readonly Guid _appId;

    public AppScopedFileSystem(AppFileStorageService storage, Guid userId, Guid appId)
    {
        _storage = storage;
        _userId  = userId;
        _appId   = appId;
    }

    public Task<string?> ReadFileAsync(string relativePath) =>
        _storage.ReadAppFileAsync(_userId, _appId, relativePath);

    public Task WriteFileAsync(string relativePath, string content) =>
        _storage.WriteAppFileAsync(_userId, _appId, relativePath, content);

    public async Task DeleteFileAsync(string relativePath)
    {
        if (relativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete index.html — it is the main app file.");

        await _storage.DeleteAppFileAsync(_userId, _appId, relativePath);
    }

    public Task<IEnumerable<string>> ListFilesAsync() =>
        _storage.GetAppFilesAsync(_userId, _appId);
}
