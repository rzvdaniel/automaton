using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Canonical policy for assigning and consuming an App deployment target's Runner.
/// </summary>
public sealed class AppRunnerAssignmentPolicy(
    ApplicationDbContext dbContext,
    HostedRunnerAppCapacityService hostedRunnerAppCapacityService,
    IAppRunnerAuthorizationPolicy runnerAuthorizationPolicy,
    IAppDeploymentTargetResolver targetResolver)
{
    public async Task AssignAsync(
        WebApp app,
        Guid? runnerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var target = await GetOrCreateDefaultTargetAsync(app, cancellationToken);
        await AssignAsync(app, target, runnerId, userId, cancellationToken);
    }

    public async Task AssignAsync(
        WebApp app,
        AppDeploymentTarget target,
        Guid? runnerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (target.WebAppId != app.Id)
        {
            throw new InvalidOperationException(
                "The deployment target does not belong to this App.");
        }

        if (target.ExecutionRunnerId != runnerId &&
            (target.LegacyControllerRuntimeDeployed || target.GenericRuntimeDeployed))
        {
            throw new AppDeploymentTargetConflictException(
                "This target owns a deployed runtime. Use the deployment-target assignment lifecycle and explicitly clean up the runtime before changing its Runner.");
        }

        if (!runnerId.HasValue)
        {
            target.ExecutionRunnerId = null;
            target.LegacyControllerRuntimeDeployed = false;
            target.GenericRuntimeDeployed = false;
            target.Status = DeploymentTargetStatus.Unassigned;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        var runner = await runnerAuthorizationPolicy.AuthorizeAsync(
            app,
            runnerId.Value,
            userId,
            cancellationToken);
        if (target.ExecutionRunnerId != runner.Id)
        {
            PrivilegedHostAssignmentGuard.EnsureApproved(target, runner);
            target.LegacyControllerRuntimeDeployed = false;
            target.GenericRuntimeDeployed = false;
        }
        await hostedRunnerAppCapacityService.AssignAsync(target, runnerId.Value, cancellationToken);
    }

    public async Task<Runner> GetAssignedRunnerAsync(
        Guid appId,
        Guid userId,
        bool requireOnline,
        string? targetSlug = null,
        CancellationToken cancellationToken = default)
    {
        var target = await targetResolver.ResolveAsync(
            appId,
            targetSlug,
            userId,
            cancellationToken);
        var runner = target.ExecutionRunner!;

        PrivilegedHostAssignmentGuard.EnsureApproved(target, runner);

        if (requireOnline &&
            !RunnerPresenceEvaluator.IsOnline(
                runner.ConnectionId,
                runner.LastHeartbeatAtUtc,
                DateTime.UtcNow))
        {
            throw new InvalidOperationException("The assigned Runner is unavailable.");
        }

        return runner;
    }

    private async Task<AppDeploymentTarget> GetOrCreateDefaultTargetAsync(
        WebApp app,
        CancellationToken cancellationToken)
    {
        var trackedTarget = app.DeploymentTargets.SingleOrDefault(item => item.IsDefault);
        if (trackedTarget != null)
        {
            return trackedTarget;
        }

        if (dbContext.Entry(app).State != EntityState.Added)
        {
            var persistedTarget = await dbContext.AppDeploymentTargets
                .SingleOrDefaultAsync(
                    item => item.WebAppId == app.Id && item.IsDefault,
                    cancellationToken);
            if (persistedTarget != null)
            {
                return persistedTarget;
            }
        }

        var target = AppDeploymentTarget.CreateLive(app.Id);
        app.DeploymentTargets.Add(target);
        return target;
    }

}
