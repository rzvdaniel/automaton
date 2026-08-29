using Automaton.Api.Data;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class AppDeploymentTargetStateService(ApplicationDbContext dataContext)
{
    public async Task<DeploymentTargetStatus> ClaimMutationAsync(
        Guid appId,
        Guid deploymentTargetId,
        string conflictMessage,
        CancellationToken cancellationToken = default)
    {
        var originalStatus = await dataContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(target => target.Id == deploymentTargetId && target.WebAppId == appId)
            .Select(target => (DeploymentTargetStatus?)target.Status)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Deployment target '{deploymentTargetId}' does not belong to app '{appId}'.");
        if (originalStatus == DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(conflictMessage);
        }

        if (dataContext.Database.IsRelational())
        {
            var now = DateTimeOffset.UtcNow;
            var updated = await dataContext.AppDeploymentTargets
                .Where(target =>
                    target.Id == deploymentTargetId &&
                    target.WebAppId == appId &&
                    target.Status == originalStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(target => target.Status, DeploymentTargetStatus.Deploying)
                    .SetProperty(target => target.UpdatedAt, now), cancellationToken);
            if (updated != 1)
            {
                throw new AppDeploymentTargetConflictException(conflictMessage);
            }
            return originalStatus;
        }

        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status != originalStatus)
        {
            throw new AppDeploymentTargetConflictException(conflictMessage);
        }
        target.Status = DeploymentTargetStatus.Deploying;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dataContext.SaveChangesAsync(cancellationToken);
        return originalStatus;
    }

    public async Task ReleaseMutationAsync(
        Guid appId,
        Guid deploymentTargetId,
        DeploymentTargetStatus status,
        CancellationToken cancellationToken = default)
    {
        if (dataContext.Database.IsRelational())
        {
            await dataContext.AppDeploymentTargets
                .Where(target =>
                    target.Id == deploymentTargetId &&
                    target.WebAppId == appId &&
                    target.Status == DeploymentTargetStatus.Deploying)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(target => target.Status, status)
                    .SetProperty(target => target.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status == DeploymentTargetStatus.Deploying)
        {
            target.Status = status;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await dataContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkStartedAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (dataContext.Database.IsRelational())
        {
            var updated = await dataContext.AppDeploymentTargets
                .Where(target =>
                    target.Id == deploymentTargetId &&
                    target.WebAppId == appId &&
                    target.Status != DeploymentTargetStatus.Deploying)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(target => target.Status, DeploymentTargetStatus.Deploying)
                    .SetProperty(target => target.LastDeploymentAt, now)
                    .SetProperty(target => target.UpdatedAt, now), cancellationToken);
            if (updated == 1)
            {
                return;
            }

            if (await dataContext.AppDeploymentTargets.AnyAsync(
                    target => target.Id == deploymentTargetId && target.WebAppId == appId,
                    cancellationToken))
            {
                throw new AppDeploymentTargetConflictException(
                    "A deployment is already active for this target.");
            }

            throw new InvalidOperationException(
                $"Deployment target '{deploymentTargetId}' does not belong to app '{appId}'.");
        }

        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status == DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(
                "A deployment is already active for this target.");
        }
        target.Status = DeploymentTargetStatus.Deploying;
        target.LastDeploymentAt = now;
        target.UpdatedAt = now;
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool success,
        bool runtimeDeployed,
        CancellationToken cancellationToken = default)
    {
        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status != DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(
                "This deployment is no longer the active operation for the target.");
        }
        var now = DateTimeOffset.UtcNow;
        target.Status = success ? DeploymentTargetStatus.Running : DeploymentTargetStatus.Failed;
        target.UpdatedAt = now;
        if (success)
        {
            target.RuntimeDeployed = runtimeDeployed;
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
