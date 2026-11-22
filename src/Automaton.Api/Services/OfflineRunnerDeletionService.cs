using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Deletes a Runner that is offline for good together with the apps assigned to it, skipping cleanup on its machine.
/// </summary>
public sealed class OfflineRunnerDeletionService(
    ApplicationDbContext dbContext,
    AppMutationService appMutationService,
    RunnerDeletionService runnerDeletionService,
    RunnerDeletionPolicy deletionPolicy,
    ResourceAuthorizationService authorizationService)
{
    /// <summary>
    /// Returns the host cleanup that was skipped, for the administrator to finish by hand, or null when the Runner does not exist.
    /// Refuses before deleting anything when the caller may not delete the Runner or one of its apps.
    /// </summary>
    public async Task<IReadOnlyList<string>?> DeleteAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var runner = await dbContext.Runners.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);
        if (runner is null) return null;
        if (RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException("The Runner is online. Use the normal removal, which cleans up its machine.");

        var appIds = await dbContext.AppDeploymentTargets
            .Where(target => target.ExecutionRunnerId == runnerId)
            .Select(target => target.WebAppId)
            .Distinct()
            .ToListAsync(cancellationToken);
        await deletionPolicy.EnsureCanDeleteAsync(runnerId, cancellationToken, abandonOffline: true);
        foreach (var appId in appIds)
            if (!await authorizationService.CanDeleteAppAsync(appId, cancellationToken))
                throw new InvalidOperationException(
                    $"You cannot delete app {appId}, which is assigned to this Runner. Nothing was deleted.");
        var skipped = new List<string>();
        foreach (var appId in appIds)
            skipped.AddRange(await appMutationService.Remove(appId, abandonOfflineRuntimes: true));

        await runnerDeletionService.DeleteAsync(runnerId, cancellationToken, abandonOffline: true);
        return skipped;
    }
}
