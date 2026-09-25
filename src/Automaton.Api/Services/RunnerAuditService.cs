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
        Guid? runnerHostId,
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
                RunnerHostId = runnerHostId,
                Changes = changes
            }
        }, cancellationToken);

    public Task WriteHostedRunnerRegistrationCompletedAsync(
        HostedRunner hostedRunner,
        Runner runner,
        string action,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = hostedRunner.Id,
            EventType = action == HostedRunnerDeploymentActions.Reprovision
                ? AuditEventTypes.RunnerReprovisionSucceeded
                : AuditEventTypes.HostedRunnerProvisioningSucceeded,
            Outcome = AuditOutcomes.Success,
            Message = $"Hosted runner '{hostedRunner.Name}' registered successfully after {action.ToLowerInvariant()}",
            TargetUserId = hostedRunner.UserId,
            Metadata = new { hostedRunner.Name, hostedRunner.RunnerHostId, RunnerId = runner.Id, DeploymentAction = action }
        }, cancellationToken);
}
