using Automaton.Api.Data;
using Automaton.Core.Models;
using Automaton.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerAppAssignmentService
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly BackendRouteConfigService routeConfigService;
    private readonly ILogger<ManagedRunnerAppAssignmentService> logger;

    public ManagedRunnerAppAssignmentService(
        ApplicationDbContext dbContext,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        ResourceAuthorizationService authorizationService,
        BackendRouteConfigService routeConfigService,
        ILogger<ManagedRunnerAppAssignmentService> logger)
    {
        this.dbContext = dbContext;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.authorizationService = authorizationService;
        this.routeConfigService = routeConfigService;
        this.logger = logger;
    }

    public async Task<int> ReconcileHostedRunnerIdentityAsync(
        Guid userId,
        Guid runnerId,
        CancellationToken cancellationToken = default)
    {
        var hostedRunner = await dbContext.HostedRunners.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RunnerId == runnerId && x.UserId == userId && x.DeletedAt == null,
                cancellationToken);
        if (hostedRunner == null) return 0;

        var previousRunnerIds = dbContext.RunnerEnrollments
            .Where(x => x.HostedRunnerId == hostedRunner.Id && x.RunnerId != null && x.RunnerId != runnerId)
            .Select(x => x.RunnerId!.Value);
        var accessibleAppIds = authorizationService.GetAccessibleAppsQuery(userId).Select(x => x.Id);
        var targets = await dbContext.AppDeploymentTargets
            .Where(x => x.ExecutionRunnerId != null && previousRunnerIds.Contains(x.ExecutionRunnerId.Value)
                && accessibleAppIds.Contains(x.WebAppId))
            .ToListAsync(cancellationToken);

        // Enrollment history proves ownership of the same persisted runtime, not an ordinary Runner move.
        foreach (var target in targets)
        {
            target.ExecutionRunnerId = runnerId;
        }

        if (targets.Count > 0) await dbContext.SaveChangesAsync(cancellationToken);

        // Reconnect also retries routing if an earlier refresh failed after the identity was saved.
        if (await dbContext.AppDeploymentTargets.AnyAsync(
                x => x.ExecutionRunnerId == runnerId && accessibleAppIds.Contains(x.WebAppId), cancellationToken))
        {
            try
            {
                await routeConfigService.RefreshAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Route publication failed for Hosted Runner {HostedRunnerId} and Runner {RunnerId}; persisted assignments are retained",
                    hostedRunner.Id, runnerId);
            }
        }

        return targets.Count;
    }

    public async Task<int> AssignEligibleAppsAsync(
        Guid userId,
        Guid runnerId,
        CancellationToken cancellationToken = default)
    {
        var apps = await dbContext.WebApps
            .Include(x => x.WebAppUsers)
            .Include(x => x.DeploymentTargets)
            .Where(x =>
                x.DeploymentTargets.Any(target => target.IsDefault && target.ExecutionRunnerId == null) &&
                x.WebAppUsers.Any(u => u.UserId == userId))
            .ToListAsync(cancellationToken);

        var assignedCount = 0;
        var runner = await dbContext.Runners.AsNoTracking()
            .SingleAsync(x => x.Id == runnerId, cancellationToken);
        var supportsContainerApplicationExecution = SupportsContainerApplicationExecution(runner.ApiCompatibilityVersion);
        foreach (var app in apps)
        {
            if (!HasContainerExecutionCommand(app.ApplicationContractJson))
            {
                continue;
            }

            if (!supportsContainerApplicationExecution)
            {
                continue;
            }

            try
            {
                await runnerAssignmentPolicy.AssignAsync(app, runnerId, userId, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogInformation(ex, "Managed runner {RunnerId} could not be assigned", runnerId);
                break;
            }

            assignedCount++;
        }

        if (assignedCount > 0)
        {
            logger.LogInformation(
                "Assigned managed runner {RunnerId} to {AssignedCount} eligible app(s) for user {UserId}",
                runnerId,
                assignedCount,
                userId);
        }

        return assignedCount;
    }

    private static bool SupportsContainerApplicationExecution(string? apiCompatibilityVersion)
        => int.TryParse(apiCompatibilityVersion, out var protocol)
            && protocol >= int.Parse(RunnerApiCompatibilityVersions.ContainerApplicationExecution);

    private static bool HasContainerExecutionCommand(string? applicationContractJson)
        => ApplicationContractSerialization.Deserialize(applicationContractJson).Runtime.Command is not null;
}
