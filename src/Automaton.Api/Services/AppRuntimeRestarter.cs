using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

/// <summary>Restarts an app's running runtimes so they load the app's current secrets and credentials.</summary>
public interface IAppRuntimeRestarter
{
    /// <summary>Queues a restart on each Runner hosting a running copy of the app; returns how many were queued.</summary>
    Task<int> RestartRunningAsync(Guid appId, CancellationToken token);
}

/// <summary>
/// Queues a durable <c>restart-runtime</c> command for every running deployment target of an app. The Runner
/// starts a new container from the deployed files, which loads current secrets, and replaces the old one once
/// the new one is ready. Targets being deployed are skipped: the deployment loads current secrets itself.
/// </summary>
public sealed class AppRuntimeRestarter(
    ApplicationDbContext db,
    RunnerExecutionOperationService operations,
    UserContextService user,
    ILogger<AppRuntimeRestarter> logger) : IAppRuntimeRestarter
{
    public const string CommandKind = "restart-runtime";

    public async Task<int> RestartRunningAsync(Guid appId, CancellationToken token)
    {
        var targets = await RunningTargets(db, appId).ToListAsync(token);
        var queued = 0;
        foreach (var target in targets)
        {
            try
            {
                await operations.QueueAsync(target.RunnerId, user.GetUserId(), new(null, null,
                    AppActionCommand.Create(CommandKind, new RestartAppRuntimeRequest(appId, target.Id))), token);
                queued++;
            }
            catch (InvalidOperationException error)
            {
                // A Runner being removed accepts no work; its runtime goes away with it.
                logger.LogWarning(error, "Could not queue a runtime restart for app {AppId} target {TargetId}.", appId, target.Id);
            }
        }
        return queued;
    }

    internal static IQueryable<RunningTarget> RunningTargets(ApplicationDbContext db, Guid appId)
        => db.AppDeploymentTargets
            .Where(target => target.WebAppId == appId && target.ExecutionRunnerId != null && target.GenericRuntimeDeployed
                && target.Status == DeploymentTargetStatus.Running && target.ActiveDeploymentOperationId == null && !target.CleanupPending)
            .Select(target => new RunningTarget(target.Id, target.ExecutionRunnerId!.Value));

    internal sealed record RunningTarget(Guid Id, Guid RunnerId);
}
