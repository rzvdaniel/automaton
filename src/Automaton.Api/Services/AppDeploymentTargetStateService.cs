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
                    .SetProperty(target => target.ActiveDeploymentOperationId, (Guid?)null)
                    .SetProperty(target => target.ActiveDeploymentExecutionAttemptId, (Guid?)null)
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
        target.ActiveDeploymentOperationId = null;
        target.ActiveDeploymentExecutionAttemptId = null;
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
                    .SetProperty(target => target.ActiveDeploymentOperationId, (Guid?)null)
                    .SetProperty(target => target.ActiveDeploymentExecutionAttemptId, (Guid?)null)
                    .SetProperty(target => target.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
            return;
        }

        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status == DeploymentTargetStatus.Deploying)
        {
            target.Status = status;
            target.ActiveDeploymentOperationId = null;
            target.ActiveDeploymentExecutionAttemptId = null;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await dataContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkStartedAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken = default,
        Guid? deploymentOperationId = null,
        Guid? executionAttemptId = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (dataContext.Database.IsRelational())
        {
            var updated = await dataContext.AppDeploymentTargets
                .Where(target =>
                    target.Id == deploymentTargetId &&
                    target.WebAppId == appId &&
                    (target.Status != DeploymentTargetStatus.Deploying ||
                     (deploymentOperationId.HasValue &&
                      target.ActiveDeploymentOperationId == deploymentOperationId)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(target => target.Status, DeploymentTargetStatus.Deploying)
                    .SetProperty(target => target.LastDeploymentAt, now)
                    .SetProperty(target => target.ActiveDeploymentOperationId, deploymentOperationId)
                    .SetProperty(target => target.ActiveDeploymentExecutionAttemptId, executionAttemptId)
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
        if (target.Status == DeploymentTargetStatus.Deploying &&
            (!deploymentOperationId.HasValue || target.ActiveDeploymentOperationId != deploymentOperationId))
        {
            throw new AppDeploymentTargetConflictException(
                "A deployment is already active for this target.");
        }
        target.Status = DeploymentTargetStatus.Deploying;
        target.LastDeploymentAt = now;
        target.ActiveDeploymentOperationId = deploymentOperationId;
        target.ActiveDeploymentExecutionAttemptId = executionAttemptId;
        target.UpdatedAt = now;
        await dataContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool success,
        CancellationToken cancellationToken = default,
        bool? genericRuntimeDeployed = null,
        Guid? deploymentOperationId = null,
        Guid? executionAttemptId = null)
    {
        var target = await GetTargetAsync(appId, deploymentTargetId, cancellationToken);
        if (target.Status != DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(
                "This deployment is no longer the active operation for the target.");
        }
        if (deploymentOperationId.HasValue && target.ActiveDeploymentOperationId != deploymentOperationId)
        {
            throw new AppDeploymentTargetConflictException(
                "This deployment is no longer the active operation for the target.");
        }
        if (executionAttemptId.HasValue && target.ActiveDeploymentExecutionAttemptId != executionAttemptId)
        {
            throw new AppDeploymentTargetConflictException(
                "This deployment is no longer the active execution attempt for the target.");
        }
        var now = DateTimeOffset.UtcNow;
        target.Status = success ? DeploymentTargetStatus.Running : DeploymentTargetStatus.Failed;
        target.ActiveDeploymentOperationId = null;
        target.ActiveDeploymentExecutionAttemptId = null;
        target.UpdatedAt = now;
        if (success)
        {
            if (genericRuntimeDeployed.HasValue)
                target.GenericRuntimeDeployed = genericRuntimeDeployed.Value;
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
