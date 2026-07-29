using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Canonical policy for assigning and consuming an App deployment target's Runner.
/// Infrastructure Runner assignment remains unsupported until the reviewed admin
/// authorization flow is implemented.
/// </summary>
public sealed class AppRunnerAssignmentPolicy(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    HostedRunnerAppCapacityService hostedRunnerAppCapacityService)
{
    public async Task AssignAsync(
        WebApp app,
        Guid? runnerId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var target = await GetOrCreateDefaultTargetAsync(app, cancellationToken);

        if (!runnerId.HasValue)
        {
            target.ExecutionRunnerId = null;
            target.Status = DeploymentTargetStatus.Unassigned;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        await ResolveEligibleRunnerAsync(app, runnerId.Value, userId, cancellationToken);
        await hostedRunnerAppCapacityService.AssignAsync(target, runnerId.Value, cancellationToken);
    }

    public async Task<Runner> GetAssignedRunnerAsync(
        Guid appId,
        bool requireOnline,
        CancellationToken cancellationToken = default)
    {
        var app = await dbContext.WebApps
            .AsNoTracking()
            .Include(item => item.DeploymentTargets)
            .SingleOrDefaultAsync(item => item.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");

        var target = app.DeploymentTargets.SingleOrDefault(item => item.IsDefault)
            ?? throw new InvalidOperationException("The App has no default deployment target.");

        if (!target.ExecutionRunnerId.HasValue)
        {
            throw new InvalidOperationException("No Runner is assigned to the default deployment target.");
        }

        var runner = await ResolvePersistedRunnerAsync(
            app,
            target.ExecutionRunnerId.Value,
            cancellationToken);

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

        var target = AppDeploymentTarget.CreateDefault(app.Id);
        app.DeploymentTargets.Add(target);
        return target;
    }

    private async Task<Runner> ResolveEligibleRunnerAsync(
        WebApp app,
        Guid runnerId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var runner = await authorizationService
            .GetRunnersQueryByContext(app.ProjectId, userId)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runnerId, cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException(
                "Execution Runner not found or you do not have access to it.");
        }

        EnsureEligibleForApplicationExecution(runner);
        return runner;
    }

    private async Task<Runner> ResolvePersistedRunnerAsync(
        WebApp app,
        Guid runnerId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Runners
            .AsNoTracking()
            .Where(item => item.Id == runnerId);

        query = app.ProjectId.HasValue
            ? query.Where(item => dbContext.ProjectRunners.Any(link =>
                link.ProjectId == app.ProjectId.Value &&
                link.RunnerId == item.Id))
            : query.Where(item => item.RunnerUsers.Any(runnerUser =>
                dbContext.WebAppUsers.Any(appUser =>
                    appUser.WebAppId == app.Id &&
                    appUser.UserId == runnerUser.UserId)));

        var runner = await query.SingleOrDefaultAsync(cancellationToken);
        if (runner == null)
        {
            throw new InvalidOperationException(
                "The assigned Runner is invalid or no longer accessible to this App.");
        }

        EnsureEligibleForApplicationExecution(runner);
        return runner;
    }

    private static void EnsureEligibleForApplicationExecution(Runner runner)
    {
        if (RunnerCapabilities.RequiresAdministrator(runner.CapabilitiesJson, runner.Purpose))
        {
            throw new InvalidOperationException(
                "Infrastructure Runners cannot be assigned to App execution.");
        }

        if (!RunnerCapabilities.CanExecuteApplications(runner.CapabilitiesJson, runner.Purpose))
        {
            throw new InvalidOperationException(
                "The selected Runner is not eligible for App execution.");
        }
    }
}
