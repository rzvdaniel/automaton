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
        AppDeploymentTarget target, bool abandonOfflineRuntime, CancellationToken cancellationToken,
        List<string>? skippedHostCleanup = null)
    {
        if (abandonOfflineRuntime && !RunnerPresenceEvaluator.HasRecentHeartbeat(
                target.ExecutionRunner?.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            skippedHostCleanup?.Add(
                $"Environment '{target.Name}' on Runner '{target.ExecutionRunner?.Name}': containers and data may remain on that machine.");
            return false;
        }

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
            var result = await gateway.SendBoundedAsync<CleanupAppDeploymentTargetResult>(runner!.Id,
                deleteApplicationStorage ? "cleanup-app-resources" : "cleanup-target-resources",
                new CleanupAppDeploymentTargetRequest(target.WebAppId, target.Id, deleteApplicationStorage,
                    UnpublishStaticSite: target.IsDefault || deleteApplicationStorage), cancellationToken);
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
        catch (RunnerNotRespondingException)
        {
            // A Runner that stopped recently still looks online; answer now instead of waiting on it. Giving up
            // cancels the queued command, so nothing is removed and the owner deletes again once the Runner is back.
            await RecordCleanupFailureAsync(
                target,
                "The Runner did not answer in time and may be offline. Nothing was removed; delete the app again when the Runner is back online.",
                CancellationToken.None);
        }
        catch (InvalidOperationException exception) when (!string.IsNullOrWhiteSpace(exception.Message))
        {
            // The Runner answered but could not perform cleanup; an outdated Runner rejects newer commands.
            await RecordCleanupFailureAsync(
                target,
                $"The Runner could not clean up this target: {exception.Message} If the Runner is outdated, upgrade it from Studio > Runners.",
                CancellationToken.None);
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
