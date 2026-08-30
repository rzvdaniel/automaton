using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class ManagedRunnerAppAssignmentService
{
    private readonly ApplicationDbContext dbContext;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly ILogger<ManagedRunnerAppAssignmentService> logger;

    public ManagedRunnerAppAssignmentService(
        ApplicationDbContext dbContext,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        ILogger<ManagedRunnerAppAssignmentService> logger)
    {
        this.dbContext = dbContext;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.logger = logger;
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
        foreach (var app in apps)
        {
            var application = ApplicationContractSerialization.Deserialize(app.ApplicationContractJson);
            var hasLegacyControllerRuntime = app.DeploymentTargets.Any(
                target => target.IsDefault && target.LegacyControllerRuntimeDeployed);
            if (application.Runtime.Command is null && !hasLegacyControllerRuntime)
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
}
