using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Api.Services;

/// <summary>Atomically admits a deployment and publishes its durable dispatch request.</summary>
internal sealed class DeploymentOperationAdmission(ApplicationDbContext db,
    ITransactionalEventExecutor transactions, IEventPublisher events)
{
    public Task SaveAsync(DeploymentOperation operation, CancellationToken token)
        => transactions.ExecuteAsync(async ct =>
        {
            await using var admission = await new AppDeletionClaimService(db).OpenAdmissionAsync(operation.WebAppId, ct);
            db.DeploymentOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            await events.PublishAsync(new DeploymentPackageRequestedEvent { OperationId = operation.Id }, ct);
            if (admission is not null) await admission.CommitAsync(ct);
        }, token);
}
