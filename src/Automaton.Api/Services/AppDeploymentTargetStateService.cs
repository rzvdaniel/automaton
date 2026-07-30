using Automaton.Api.Data;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class AppDeploymentTargetStateService(ApplicationDbContext dataContext)
{
    public async Task MarkStartedAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken = default)
    {
        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        target.Status = DeploymentTargetStatus.Deploying;
        target.LastDeploymentAt = now;
        target.UpdatedAt = now;
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool success,
        bool deployFrontend,
        bool deployBackend,
        CancellationToken cancellationToken = default)
    {
        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        target.Status = success ? DeploymentTargetStatus.Running : DeploymentTargetStatus.Failed;
        target.UpdatedAt = now;
        if (success)
        {
            target.FrontendDeployed |= deployFrontend;
            target.BackendDeployed |= deployBackend;
            target.LastSuccessfulDeploymentAt = now;
        }
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Entities.AppDeploymentTarget> GetTargetAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken)
    {
        return await dataContext.AppDeploymentTargets.SingleOrDefaultAsync(
                target => target.Id == deploymentTargetId && target.WebAppId == appId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Deployment target '{deploymentTargetId}' does not belong to app '{appId}'.");
    }
}
