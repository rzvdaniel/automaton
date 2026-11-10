using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

/// <summary>Restarts an app's running runtimes so they load the app's current secrets and credentials.</summary>
public interface IAppRuntimeRestarter
{
    /// <summary>
    /// Queues a restart on each Runner hosting a running copy of the app; returns how many were queued. With
    /// <paramref name="wait"/> it waits for each Runner and returns how many restarted, or throws <see cref="RuntimeCommandFailedException"/>.
    /// </summary>
    Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false);
}

/// <summary>
/// Queues a durable <c>restart-runtime</c> command for every running deployment target of an app. The Runner
/// starts a new container from the deployed files, which loads current secrets, and replaces the old one once
/// the new one is ready. A target being deployed is included too: its deployment may have loaded the old
/// credential, and the Runner runs the restart after the deployment finishes.
/// </summary>
public sealed class AppRuntimeRestarter(
    ApplicationDbContext db,
    RunnerExecutionOperationService operations,
    UserContextService user,
    ILogger<AppRuntimeRestarter> logger) : IAppRuntimeRestarter
{
    public const string CommandKind = "restart-runtime";
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(150);

    public async Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false)
    {
        var targets = await RunningTargets(db, appId).ToListAsync(token);
        var queued = 0;
        var failures = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                RunnerExecutionRequest request = new(null, null,
                    AppActionCommand.Create(CommandKind, new RestartAppRuntimeRequest(appId, target.Id)));
                if (wait) await operations.QueueAndWaitAsync(target.RunnerId, user.GetUserId(), request, RestartTimeout, token);
                else await operations.QueueAsync(target.RunnerId, user.GetUserId(), request, token);
                queued++;
            }
            catch (RuntimeCommandFailedException error) when (wait)
            {
                failures.Add(error.Message);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The permission change is already saved; a copy that misses the restart gets it at its next start.
                logger.LogWarning(error, "Could not queue a runtime restart for app {AppId} target {TargetId}.", appId, target.Id);
                if (wait) failures.Add("The Runner could not be told to restart the app. Try again when the Runner is online.");
            }
        }
        if (failures.Count > 0) throw new RuntimeCommandFailedException(string.Join(" ", failures.Distinct()));
        return queued;
    }

    internal static IQueryable<RunningTarget> RunningTargets(ApplicationDbContext db, Guid appId)
        => db.AppDeploymentTargets
            .Where(target => target.WebAppId == appId && target.ExecutionRunnerId != null && target.GenericRuntimeDeployed
                && (target.Status == DeploymentTargetStatus.Running || target.Status == DeploymentTargetStatus.Deploying) && !target.CleanupPending)
            .Select(target => new RunningTarget(target.Id, target.ExecutionRunnerId!.Value));

    /// <summary>Running targets plus those whose last deploy failed but may still have a runtime on the Runner.</summary>
    internal static IQueryable<RunningTarget> PausableTargets(ApplicationDbContext db, Guid appId)
        => db.AppDeploymentTargets
            .Where(target => target.WebAppId == appId && target.ExecutionRunnerId != null && !target.CleanupPending
                && (target.Status == DeploymentTargetStatus.Running || target.Status == DeploymentTargetStatus.Deploying
                    || target.Status == DeploymentTargetStatus.Failed))
            .Select(target => new RunningTarget(target.Id, target.ExecutionRunnerId!.Value));

    internal sealed record RunningTarget(Guid Id, Guid RunnerId);
}
