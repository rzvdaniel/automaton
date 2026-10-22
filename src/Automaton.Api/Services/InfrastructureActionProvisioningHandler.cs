using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Logging;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Applies terminal results from durable Infrastructure Command callbacks.</summary>
public sealed class InfrastructureActionProvisioningHandler(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    IOptions<HostedRunnerOptions> hostedRunnerOptions,
    IOptions<StorageTelemetryOptions> storageTelemetryOptions,
    HostedRunnerBrokerProvisioner brokerProvisioner,
    RunnerExecutionOperationService executions)
{
    private static readonly string[] ActiveDeploymentStatuses =
    [
        HostedRunnerDeploymentStatuses.Pending,
        HostedRunnerDeploymentStatuses.Queued,
        HostedRunnerDeploymentStatuses.Dispatching,
        HostedRunnerDeploymentStatuses.Executing,
        HostedRunnerDeploymentStatuses.Running,
        HostedRunnerDeploymentStatuses.WaitingForRegistration,
        HostedRunnerDeploymentStatuses.WaitingForReconnect
    ];

    internal async Task<bool> TerminalizeUnexecutableAsync(
        Guid operationId,
        string actionKey,
        string reason,
        CancellationToken cancellationToken)
    {
        var operation = await db.HostedRunnerDeployments
            .Include(x => x.HostedRunner)
            .SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The infrastructure command operation was not found.");

        EnsureTerminalizable(operation.Status);

        var now = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            if (!await TryTerminalizeRelationalAsync(operation, operationId, reason, now, cancellationToken))
                return true;
        }
        else
        {
            await ApplyInMemoryTerminalizationAsync(operation, reason, now, cancellationToken);
        }

        await WriteTerminalizationAuditAsync(operation, operationId, actionKey, reason, cancellationToken);
        return true;
    }

    private static void EnsureTerminalizable(string status)
    {
        if (ActiveDeploymentStatuses.Contains(status, StringComparer.Ordinal) || IsTerminalDeployment(status))
            return;
        throw new InvalidOperationException("The infrastructure command operation is not in a terminalizable state.");
    }

    private async Task<bool> TryTerminalizeRelationalAsync(
        HostedRunnerDeployment operation,
        Guid operationId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var affected = await db.HostedRunnerDeployments
            .Where(x => x.Id == operationId && ActiveDeploymentStatuses.Contains(x.Status))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Failed)
                .SetProperty(x => x.Error, reason)
                .SetProperty(x => x.FinishedAt, now), cancellationToken);
        if (affected != 1)
            return await ConfirmTerminalizationRaceAsync(operationId, cancellationToken);

        var hostedRunnerAffected = await db.HostedRunners
            .Where(x => x.Id == operation.HostedRunnerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, GetFailureStatus(operation.Action))
                .SetProperty(x => x.LastError, reason)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (hostedRunnerAffected != 1)
            throw new InvalidOperationException("The hosted Runner could not be terminalized with its infrastructure command.");
        return true;
    }

    private async Task<bool> ConfirmTerminalizationRaceAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var current = await db.HostedRunnerDeployments.AsNoTracking()
            .SingleAsync(x => x.Id == operationId, cancellationToken);
        if (IsTerminalDeployment(current.Status))
            return false;
        throw new InvalidOperationException("The infrastructure command operation changed state during terminalization.");
    }

    private async Task ApplyInMemoryTerminalizationAsync(
        HostedRunnerDeployment operation,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        operation.Status = HostedRunnerDeploymentStatuses.Failed;
        operation.Error = reason;
        operation.FinishedAt = now;
        operation.HostedRunner.Status = GetFailureStatus(operation.Action);
        operation.HostedRunner.LastError = reason;
        operation.HostedRunner.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GetFailureStatus(string action)
        => action == HostedRunnerDeploymentActions.Deprovision
            ? HostedRunnerStatuses.DeleteFailed
            : HostedRunnerStatuses.Failed;

    private async Task WriteTerminalizationAuditAsync(
        HostedRunnerDeployment operation,
        Guid operationId,
        string actionKey,
        string reason,
        CancellationToken cancellationToken)
    {
        var eventType = operation.Action switch
        {
            HostedRunnerDeploymentActions.Restart => AuditEventTypes.RunnerRestartFailed,
            HostedRunnerDeploymentActions.Deprovision => AuditEventTypes.HostedRunnerDeleteFailed,
            _ => AuditEventTypes.HostedRunnerProvisioningFailed
        };
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Failed,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = operation.HostedRunnerId,
            EventType = eventType,
            Outcome = AuditOutcomes.Failed,
            Message = $"Hosted runner '{operation.HostedRunner.Name}' infrastructure command was terminalized without execution.",
            TargetUserId = operation.HostedRunner.UserId,
            Metadata = new { OperationId = operationId, operation.Action, ActionKey = actionKey, Reason = reason },
            FailOnError = true
        }, cancellationToken);
    }

    private static bool IsTerminalDeployment(string status) => status is
        HostedRunnerDeploymentStatuses.Succeeded or HostedRunnerDeploymentStatuses.Failed
        or HostedRunnerDeploymentStatuses.Skipped;

    internal async Task ApplySuccessfulOperationAsync(
        HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        string actionKey,
        JsonElement result,
        CancellationToken cancellationToken)
    {
        if (actionKey == "delete-hosted-runner" && hostedRunner.RunnerId is Guid runnerId)
        {
            await executions.TerminateRunnerAsync(runnerId, cancellationToken);
            await brokerProvisioner.RevokeAsync(runnerId, cancellationToken);
        }

        if (actionKey != StorageTelemetryActions.MeasureHostedRunnerStorage)
            hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        deployment.Error = null;

        if (actionKey == "restart-hosted-runner")
        {
            ApplyRestartResult(hostedRunner, deployment);
            return;
        }

        if (actionKey == StorageTelemetryActions.MeasureHostedRunnerStorage)
            await ApplyStorageVerificationResultAsync(hostedRunner, deployment, result, cancellationToken);

        deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
        deployment.FinishedAt = DateTime.UtcNow;
        deployment.Logs = $"Trusted infrastructure Action '{actionKey}' completed.";
        if (actionKey == "delete-hosted-runner")
            MarkHostedRunnerDeleted(hostedRunner);
        await WriteLifecycleAuditAsync(hostedRunner, actionKey, succeeded: true, null, cancellationToken);
    }

    private void ApplyRestartResult(HostedRunner hostedRunner, HostedRunnerDeployment deployment)
    {
        deployment.ExpectedRunnerId ??= hostedRunner.RunnerId;
        if (!deployment.ExpectedRunnerId.HasValue)
            throw new InvalidOperationException("The restart operation has no expected Runner identity.");
        var now = DateTime.UtcNow;
        var timeoutSeconds = Math.Clamp(hostedRunnerOptions.Value.RestartReconnectTimeoutSeconds, 30, 900);
        deployment.Status = HostedRunnerDeploymentStatuses.WaitingForReconnect;
        deployment.ReconnectAfterUtc ??= deployment.StartedAt;
        deployment.ReconnectDeadlineUtc = now.AddSeconds(timeoutSeconds);
        deployment.Logs = "Restart command completed; waiting for the hosted Runner to reconnect and heartbeat.";
    }

    private async Task ApplyStorageVerificationResultAsync(
        HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        JsonElement result,
        CancellationToken cancellationToken)
        => await HostedRunnerStorageVerificationResultApplier.ApplyAsync(
            db, hostedRunner, deployment, result, storageTelemetryOptions.Value, cancellationToken);

    private static void MarkHostedRunnerDeleted(HostedRunner hostedRunner)
    {
        hostedRunner.ApiKeyId = null;
        hostedRunner.RunnerId = null;
        hostedRunner.ContainerId = null;
        hostedRunner.RuntimeEndpoint = null;
        hostedRunner.RuntimePort = null;
        hostedRunner.Status = HostedRunnerStatuses.Deleted;
        hostedRunner.DeletedAt = DateTime.UtcNow;
    }

    private Task WriteLifecycleAuditAsync(HostedRunner runner, string actionKey, bool succeeded, string? error, CancellationToken cancellationToken)
    {
        var eventType = GetLifecycleEventType(actionKey, succeeded);
        return auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = succeeded ? AuditActions.Succeeded : AuditActions.Failed,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = runner.Id,
            EventType = eventType,
            Outcome = succeeded ? AuditOutcomes.Success : AuditOutcomes.Failed,
            Message = $"Hosted runner '{runner.Name}' infrastructure operation {(succeeded ? "succeeded" : "failed")}",
            TargetUserId = runner.UserId,
            Metadata = new { runner.Name, runner.RunnerHostId, ActionKey = actionKey, Error = error }
        }, cancellationToken);
    }

    private static string GetLifecycleEventType(string actionKey, bool succeeded)
        => (actionKey, succeeded) switch
        {
            ("restart-hosted-runner", true) => AuditEventTypes.RunnerRestartSucceeded,
            ("restart-hosted-runner", false) => AuditEventTypes.RunnerRestartFailed,
            ("delete-hosted-runner", true) => AuditEventTypes.HostedRunnerDeleted,
            ("delete-hosted-runner", false) => AuditEventTypes.HostedRunnerDeleteFailed,
            (StorageTelemetryActions.MeasureHostedRunnerStorage, true) => AuditEventTypes.HostedRunnerStorageVerificationSucceeded,
            (StorageTelemetryActions.MeasureHostedRunnerStorage, false) => AuditEventTypes.HostedRunnerStorageVerificationFailed,
            (_, true) => AuditEventTypes.HostedRunnerProvisioningSucceeded,
            _ => AuditEventTypes.HostedRunnerProvisioningFailed
        };

}
