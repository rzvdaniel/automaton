using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Data;

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
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken, bool hostCleanupConfirmed = false)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        await deletionPolicy.EnsureCanDeleteAsync(id, cancellationToken);

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
            return;

        if (!entity.DeletionRequestedAtUtc.HasValue || !hostCleanupConfirmed)
            throw new InvalidOperationException("Prepare Runner removal and confirm its host cleanup before deleting the registration.");
        if (RunnerPresenceEvaluator.IsOnline(entity.ConnectionId, entity.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException("The Runner is still online. Run its uninstall command on the server before confirming removal.");

        await executions.RevokeAsync(id, cancellationToken);
        if (entity.ApiKeyId is Guid keyId)
        {
            var key = await dbContext.ApiKeys.SingleOrDefaultAsync(key => key.Id == keyId, cancellationToken);
            if (key is not null) key.IsActive = false;
        }
        dbContext.Runners.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        await RecordDeletedAsync(entity, cancellationToken);
    }

    private Task RecordDeletedAsync(Runner entity, CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.Runner,
            EntityId = entity.Id,
            EventType = AuditEventTypes.RunnerDeleted,
            Message = $"Runner '{entity.Name}' was deleted after owner-confirmed host cleanup",
            Outcome = AuditOutcomes.Success,
            ActorUserId = userContextService.GetUserId(),
            Metadata = new { entity.Name, entity.Version, entity.MachineName, entity.IpAddress }
        }, cancellationToken);
}
