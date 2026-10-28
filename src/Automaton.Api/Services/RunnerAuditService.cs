using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

/// <summary>
/// Writes audit events for Runner registration lifecycle transitions.
/// </summary>
public sealed class RunnerAuditService(IAuditEventService auditEventService)
{
    public Task WriteCreatedAsync(
        Runner runner,
        Guid actorUserId,
        int changes,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.Runner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.RunnerCreated,
            Message = $"Runner '{runner.Name}' was created",
            Outcome = AuditOutcomes.Success,
            ActorUserId = actorUserId,
            Metadata = new
            {
                runner.Name,
                runner.Version,
                runner.MachineName,
                runner.IpAddress,
                runner.Purpose,
                Changes = changes
            }
        }, cancellationToken);

}
