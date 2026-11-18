using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Deletes a Runner that is offline for good together with the apps assigned to it, skipping cleanup on its machine.
/// </summary>
public sealed class OfflineRunnerDeletionService(
    ApplicationDbContext dbContext,
    AppMutationService appMutationService,
    RunnerDeletionService runnerDeletionService)
{
    /// <summary>Returns the host cleanup that was skipped, for the administrator to finish by hand.</summary>
    public async Task<IReadOnlyList<string>> DeleteAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var runner = await dbContext.Runners.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);
        if (runner is null) return [];
        if (RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException("The Runner is online. Use the normal removal, which cleans up its machine.");

        var appIds = await dbContext.AppDeploymentTargets
            .Where(target => target.ExecutionRunnerId == runnerId)
            .Select(target => target.WebAppId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var skipped = new List<string>();
        foreach (var appId in appIds)
            skipped.AddRange(await appMutationService.Remove(appId, abandonOfflineRuntimes: true));

        await runnerDeletionService.DeleteAsync(runnerId, cancellationToken, abandonOffline: true);
        return skipped;
    }
}
