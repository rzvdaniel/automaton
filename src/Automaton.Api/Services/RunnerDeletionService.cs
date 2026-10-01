using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Applies ownership and relationship rules when a user deletes a generic Runner.
/// </summary>
public sealed class RunnerDeletionService(
    ApplicationDbContext dbContext,
    RunnerDeletionPolicy deletionPolicy,
    IAuditEventService auditEventService,
    UserContextService userContextService,
    RunnerExecutionRevocationService executions)
{
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await deletionPolicy.EnsureCanDeleteAsync(id, cancellationToken);

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
            return;

        var auditMetadata = new
        {
            entity.Name,
            entity.Version,
            entity.MachineName,
            entity.IpAddress
        };

        var assignedTargets = await dbContext.AppDeploymentTargets
            .Where(target => target.ExecutionRunnerId == id)
            .ToListAsync(cancellationToken);
        foreach (var target in assignedTargets)
        {
            target.ExecutionRunnerId = null;
            target.ExecutionRunner = null;
            target.GenericRuntimeDeployed = false;
            target.Status = DeploymentTargetStatus.Unassigned;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await executions.RevokeAsync(id, cancellationToken);
        dbContext.Runners.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.Runner,
            EntityId = id,
            EventType = AuditEventTypes.RunnerDeleted,
            Message = $"Runner '{entity.Name}' was deleted",
            Outcome = AuditOutcomes.Success,
            ActorUserId = userContextService.GetUserId(),
            Metadata = auditMetadata
        }, cancellationToken);
    }
}
