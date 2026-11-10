using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Restarts an app's running copies so they load the current settings and secrets (the same action a deploy ends
/// with). Same permission rule as deploying and pausing: the app's owner and project members who may deploy,
/// plus administrators. A paused app stays stopped until it is resumed.
/// </summary>
public sealed class AppRestartService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    IAppRuntimeRestarter restarter,
    UserContextService user)
{
    /// <summary>Restarts the app on each Runner hosting it and waits; returns how many restarted (0 when nothing is running). Throws <see cref="RuntimeCommandFailedException"/> when a Runner cannot start it.</summary>
    public async Task<int> RestartAsync(Guid appId, bool administrator, CancellationToken ct)
    {
        var userId = user.GetUserId();
        var app = await db.WebApps.FirstOrDefaultAsync(item => item.Id == appId && item.DeletionClaimId == null, ct)
            ?? throw new KeyNotFoundException("App not found");
        if (!administrator)
        {
            if (!await authorization.CanAccessAppAsync(appId, userId, ct)) throw new KeyNotFoundException("App not found");
            if (!await authorization.CanUpdateAppAsync(appId, userId, ct))
                throw new UnauthorizedAccessException("You do not have permission to restart this app.");
        }
        if (app.IsPaused) throw new InvalidOperationException("The app is paused. Resume it to start it again.");
        return await restarter.RestartRunningAsync(appId, ct, wait: true);
    }
}
