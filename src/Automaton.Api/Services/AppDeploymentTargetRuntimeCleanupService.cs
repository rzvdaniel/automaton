using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Coordinates removal of an app runtime with its assigned Runner and records cleanup state.
/// </summary>
public sealed class AppDeploymentTargetRuntimeCleanupService(
    ApplicationDbContext dbContext,
    IRunnerActionCommands gateway)
{
    public async Task<bool> CleanupForDeletionAsync(
        AppDeploymentTarget target, bool abandonOfflineRuntime, CancellationToken cancellationToken)
    {
        if (abandonOfflineRuntime && !RunnerPresenceEvaluator.HasRecentHeartbeat(
                target.ExecutionRunner?.LastHeartbeatAtUtc, DateTime.UtcNow))
            return false;

        await CleanupRuntimeAsync(target, cancellationToken);
        return true;
    }

    public async Task CleanupRuntimeAsync(
        AppDeploymentTarget target,
        CancellationToken cancellationToken,
        bool deleteApplicationStorage = false)
    {
        target.CleanupPending = true;
        target.LastCleanupAttemptAt = DateTimeOffset.UtcNow;
        target.LastCleanupError = null;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var runner = target.ExecutionRunner;
        if (runner is null || !runner.LastHeartbeatAtUtc.HasValue || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold)
        {
            await RecordCleanupFailureAsync(
                target,
                "The assigned Runner must be online to clean up this deployed target.",
                cancellationToken);
        }

        try
        {
            var result = await gateway.SendAsync<CleanupAppDeploymentTargetResult>(runner!.Id,
                deleteApplicationStorage ? "cleanup-app" : "cleanup-target", new CleanupAppDeploymentTargetRequest(target.WebAppId, target.Id, deleteApplicationStorage), cancellationToken);
            if (!result.Success || result.AppId != target.WebAppId || result.DeploymentTargetId != target.Id)
            {
                await RecordCleanupFailureAsync(
                    target,
                    result.Error ?? "The Runner could not clean up this deployment target.",
                    cancellationToken);
            }
        }
        catch (AppDeploymentTargetConflictException)
        {
            throw;
        }
        catch (Exception)
        {
            await RecordCleanupFailureAsync(
                target,
                "Runner cleanup request failed. Retry when the Runner is online.",
                CancellationToken.None);
        }

        target.CleanupPending = false;
        target.LastCleanupError = null;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordCleanupFailureAsync(
        AppDeploymentTarget target,
        string error,
        CancellationToken cancellationToken)
    {
        target.CleanupPending = true;
        target.LastCleanupError = error.Length <= 1000 ? error : error[..1000];
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        throw new AppDeploymentTargetConflictException(target.LastCleanupError);
    }
}
