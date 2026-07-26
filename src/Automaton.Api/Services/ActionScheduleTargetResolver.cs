using Automaton.Core.Models;

namespace Automaton.Api.Services;

public interface IActionScheduleTargetResolver
{
    Task<AppActionManifest> ResolveAsync(Guid appId, string actionKey, Guid userId, bool system, CancellationToken cancellationToken);
}

public sealed class ActionScheduleTargetResolver(
    ResourceAuthorizationService authorization,
    AppFileStorageService storage,
    AppsService apps) : IActionScheduleTargetResolver
{
    public async Task<AppActionManifest> ResolveAsync(Guid appId, string actionKey, Guid userId, bool system, CancellationToken cancellationToken)
    {
        string root;
        if (system)
        {
            root = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure");
        }
        else
        {
            if (!await authorization.CanExecuteAppAsync(appId, userId, cancellationToken))
                throw new UnauthorizedAccessException("You do not have permission to operate this App.");
            var app = await apps.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
                ?? throw new KeyNotFoundException("App not found.");
            root = storage.GetAppSourceDirectoryPath(
                app.OwnerId ?? throw new InvalidOperationException("App owner could not be resolved."),
                appId);
        }

        return AppActionManifestParser.Discover(root, allowPrivilegedCapabilities: system)
            .SingleOrDefault(x => x.Key == actionKey)?.Manifest
            ?? throw new KeyNotFoundException("Action not found in the App.");
    }
}
