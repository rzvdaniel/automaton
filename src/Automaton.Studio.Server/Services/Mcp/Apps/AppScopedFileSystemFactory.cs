using System.Text.Json;
using Automaton.Mcp.Tools;

namespace Automaton.Studio.Server.Services.Mcp.Apps;

/// <summary>
/// Resolves an <see cref="AppScopedFileSystem"/> for the current request by resolving
/// userId (from HTTP context) and appId (from tool arguments or conversation context)
/// into the app's editable source directory path via <see cref="AppFileStorageService"/>.
/// </summary>
public class AppScopedFileSystemFactory : IScopedFileSystemFactory
{
    private readonly AppFileStorageService _storage;
    private readonly UserContextService _userContextService;

    public AppScopedFileSystemFactory(AppFileStorageService storage, UserContextService userContextService)
    {
        _storage            = storage;
        _userContextService = userContextService;
    }

    public IScopedFileSystem Create(JsonElement arguments, Dictionary<string, object>? context)
    {
        var userId  = _userContextService.GetUserId();
        var appId   = ResolveAppId(arguments, context);
        if (!_storage.AppExists(userId, appId))
            throw new InvalidOperationException($"App {appId} not found or not accessible.");
        var appPath = _storage.GetAppSourceDirectoryPath(userId, appId);
        return new AppScopedFileSystem(appPath);
    }

    private static Guid ResolveAppId(JsonElement arguments, Dictionary<string, object>? context)
    {
        // 1. Explicit appId argument
        if (arguments.TryGetProperty("appId", out var appIdProp))
        {
            var str = appIdProp.GetString();
            if (!string.IsNullOrEmpty(str) && Guid.TryParse(str, out var id))
                return id;
        }

        // 2. Conversation context resourceId
        if (context is not null && context.TryGetValue("resourceId", out var raw))
        {
            if (raw is Guid g) return g;
            if (raw is string s && Guid.TryParse(s, out var id)) return id;
        }

        throw new InvalidOperationException("No valid appId found in tool arguments or conversation context.");
    }
}
