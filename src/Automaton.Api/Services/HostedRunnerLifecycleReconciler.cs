using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class HostedRunnerLifecycleReconciler(
    ApplicationDbContext db,
    ITransactionalEventExecutor transactionalEventExecutor,
    IEventPublisher eventPublisher,
    IAuditEventService auditEventService,
    IOptionsSnapshot<HostedRunnerOptions> options,
    ILogger<HostedRunnerLifecycleReconciler> logger)
{
    private static readonly string[] RecoverableStatuses =
    [
        HostedRunnerDeploymentStatuses.Queued,
        HostedRunnerDeploymentStatuses.Dispatching,
        HostedRunnerDeploymentStatuses.Executing,
        HostedRunnerDeploymentStatuses.Running
    ];

    public async Task<int> ReconcileAsync(DateTime now, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Clamp(options.Value.LifecycleRecoveryDelaySeconds, 60, 3600));
        var retry = TimeSpan.FromSeconds(Math.Clamp(options.Value.LifecycleRecoveryRetrySeconds, 60, 3600));
        var recoveryCutoff = now - retry;
        var candidates = await db.HostedRunnerDeployments.AsNoTracking()
            .Where(x => RecoverableStatuses.Contains(x.Status)
                && x.StartedAt <= now - delay
                && (x.LastRecoveryAttemptUtc == null || x.LastRecoveryAttemptUtc <= recoveryCutoff))
            .OrderBy(x => x.StartedAt)
            .Select(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        var reconciled = 0;
        foreach (var id in candidates)
        {
            db.ChangeTracker.Clear();
            var operation = await db.HostedRunnerDeployments
                .Include(x => x.HostedRunner).ThenInclude(x => x.RunnerHost)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (operation == null || !RecoverableStatuses.Contains(operation.Status)) continue;
            var execution = await db.AppModuleExecutions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.HostedRunnerOperationId == id, cancellationToken);

            if (execution != null)
            {
                if (!IsTerminal(execution.Status)) continue;
                var reason = execution.Status == AppModuleExecutionStatuses.Succeeded
                    ? HostedRunnerDeploymentErrors.LifecycleResultAmbiguous
                    : execution.FailureSummary ?? HostedRunnerDeploymentErrors.LifecycleExecutionFailed;
                if (await FailAsync(operation, reason, now, cancellationToken)) reconciled++;
                continue;
            }

            if (await RepublishAsync(operation, now, recoveryCutoff, cancellationToken)) reconciled++;
        }

        if (reconciled > 0)
            logger.LogWarning("Reconciled {Count} stale hosted Runner lifecycle operations", reconciled);
        return reconciled;
    }

    private async Task<bool> RepublishAsync(
        HostedRunnerDeployment operation,
        DateTime now,
        DateTime recoveryCutoff,
        CancellationToken cancellationToken)
    {
        var hostedRunner = operation.HostedRunner;
        var host = hostedRunner.RunnerHost;
        if (host == null) return await FailAsync(operation, "The hosted Runner no longer has an assigned host.", now, cancellationToken);

        var isProvision = operation.Action is HostedRunnerDeploymentActions.Provision or HostedRunnerDeploymentActions.Reprovision;
        IReadOnlyList<Guid> grantIds = isProvision
            ? await db.ExecutionSecretGrants.AsNoTracking()
                .Where(x => x.OperationId == operation.Id
                    && x.RevokedAtUtc == null && x.ConsumedAtUtc == null
                    && x.DeliveredAtUtc == null && x.ExpiresAtUtc > now)
                .Select(x => x.Id).ToListAsync(cancellationToken)
            : [];
        if (isProvision && grantIds.Count == 0)
            return await FailAsync(operation, "The stale provisioning operation no longer has valid enrollment material.", now, cancellationToken);

        object message = isProvision
            ? new ManagedRunnerProvisionRequestedEvent
            {
                OperationId = operation.Id, HostedRunnerId = hostedRunner.Id, UserId = hostedRunner.UserId,
                RunnerHostId = host.Id, SecretGrantIds = grantIds,
                CorrelationId = $"hosted-runner-recovery:{operation.Id:N}"
            }
            : BuildInfrastructureRequest(operation, hostedRunner, host);

        var claimed = false;
        await transactionalEventExecutor.ExecuteAsync(async token =>
        {
            if (db.Database.IsRelational())
            {
                var affected = await db.HostedRunnerDeployments
                    .Where(x => x.Id == operation.Id
                        && RecoverableStatuses.Contains(x.Status)
                        && (x.LastRecoveryAttemptUtc == null || x.LastRecoveryAttemptUtc <= recoveryCutoff))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Queued)
                        .SetProperty(x => x.LastRecoveryAttemptUtc, now), token);
                if (affected != 1) return;
            }
            else
            {
                if (!RecoverableStatuses.Contains(operation.Status)
                    || operation.LastRecoveryAttemptUtc > recoveryCutoff) return;
                operation.Status = HostedRunnerDeploymentStatuses.Queued;
                operation.LastRecoveryAttemptUtc = now;
                await db.SaveChangesAsync(token);
            }
            claimed = true;
            if (message is ManagedRunnerProvisionRequestedEvent managed)
                await eventPublisher.PublishAsync(managed, token);
            else
                await eventPublisher.PublishAsync((InfrastructureActionExecutionRequestedEvent)message, token);
        }, cancellationToken);
        return claimed;
    }

    private async Task<bool> FailAsync(
        HostedRunnerDeployment operation,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var failed = false;
        await transactionalEventExecutor.ExecuteAsync(async token =>
        {
            if (db.Database.IsRelational())
            {
                var affected = await db.HostedRunnerDeployments
                    .Where(x => x.Id == operation.Id && RecoverableStatuses.Contains(x.Status))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Failed)
                        .SetProperty(x => x.Error, reason)
                        .SetProperty(x => x.FinishedAt, now), token);
                if (affected != 1) return;
                var hostedRunnerAffected = await db.HostedRunners.Where(x => x.Id == operation.HostedRunnerId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, HostedRunnerStatuses.Failed)
                        .SetProperty(x => x.LastError, reason)
                        .SetProperty(x => x.UpdatedAt, now), token);
                if (hostedRunnerAffected != 1)
                    throw new InvalidOperationException($"Hosted Runner {operation.HostedRunnerId} could not be failed during lifecycle recovery.");
            }
            else
            {
                if (!RecoverableStatuses.Contains(operation.Status)) return;
                operation.Status = HostedRunnerDeploymentStatuses.Failed;
                operation.Error = reason;
                operation.FinishedAt = now;
                operation.HostedRunner.Status = HostedRunnerStatuses.Failed;
                operation.HostedRunner.LastError = reason;
                operation.HostedRunner.UpdatedAt = now;
                await db.SaveChangesAsync(token);
            }
            failed = true;
            var eventType = operation.Action switch
            {
                HostedRunnerDeploymentActions.Restart => AuditEventTypes.RunnerRestartFailed,
                HostedRunnerDeploymentActions.Deprovision => AuditEventTypes.HostedRunnerDeleteFailed,
                _ => AuditEventTypes.HostedRunnerProvisioningFailed
            };
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Failed, EntityType = AuditEntityTypes.HostedRunner,
                EntityId = operation.HostedRunnerId, EventType = eventType,
                Outcome = AuditOutcomes.Failed, Message = $"Hosted runner '{operation.HostedRunner.Name}' lifecycle recovery failed",
                TargetUserId = operation.HostedRunner.UserId,
                Metadata = new { OperationId = operation.Id, operation.Action, Error = reason }, FailOnError = true
            }, token);
            if (operation.Action is HostedRunnerDeploymentActions.Provision or HostedRunnerDeploymentActions.Reprovision)
                await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
                {
                    HostedRunnerId = operation.HostedRunnerId, UserId = operation.HostedRunner.UserId,
                    Error = reason, CorrelationId = $"hosted-runner-recovery:{operation.Id:N}"
                }, token);
        }, cancellationToken);
        return failed;
    }

    private InfrastructureActionExecutionRequestedEvent BuildInfrastructureRequest(
        HostedRunnerDeployment operation,
        HostedRunner hostedRunner,
        RunnerHost host)
    {
        var actionKey = operation.Action switch
        {
            HostedRunnerDeploymentActions.Restart => "restart-hosted-runner",
            HostedRunnerDeploymentActions.Deprovision => "delete-hosted-runner",
            _ => throw new InvalidOperationException($"Unsupported lifecycle recovery action '{operation.Action}'.")
        };
        return new InfrastructureActionExecutionRequestedEvent
        {
            OperationId = operation.Id, HostedRunnerId = hostedRunner.Id, UserId = hostedRunner.UserId,
            RunnerHostId = host.Id, ActionKey = actionKey,
            InputJson = JsonSerializer.Serialize(new
            {
                operationId = operation.Id, hostedRunnerId = hostedRunner.Id, containerName = hostedRunner.ContainerName
            }),
            CorrelationId = $"hosted-runner-recovery:{operation.Id:N}"
        };
    }

    private static bool IsTerminal(string status) => status is
        AppModuleExecutionStatuses.Succeeded or AppModuleExecutionStatuses.Failed or
        AppModuleExecutionStatuses.TimedOut or AppModuleExecutionStatuses.Cancelled or AppModuleExecutionStatuses.Lost;
}
