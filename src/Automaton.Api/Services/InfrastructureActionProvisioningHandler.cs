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
    IOptions<StorageTelemetryOptions> storageTelemetryOptions)
{
    internal async Task ApplySuccessfulOperationAsync(
        HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        string actionKey,
        JsonElement result,
        CancellationToken cancellationToken)
    {
        if (actionKey != StorageTelemetryActions.MeasureHostedRunnerStorage)
            hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        deployment.Error = null;
        if (actionKey is "provision-hosted-runner" or "reprovision-hosted-runner")
        {
            if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !result.TryGetProperty("containerId", out var containerId)
                || containerId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(containerId.GetString())
                || !result.TryGetProperty("containerName", out var containerName)
                || containerName.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(containerName.GetString()))
                throw new InvalidOperationException("Infrastructure Action returned an invalid Hosted Runner container identity.");

            hostedRunner.ContainerId = containerId.GetString();
            hostedRunner.Status = HostedRunnerStatuses.Provisioning;
            deployment.Status = HostedRunnerDeploymentStatuses.WaitingForRegistration;
            deployment.Logs = "Infrastructure Action completed; waiting for the hosted Runner to register.";
            return;
        }

        if (actionKey == "restart-hosted-runner")
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
            return;
        }

        if (actionKey == StorageEnforcementActions.SuspendHostedRunner)
        {
            hostedRunner.Status = HostedRunnerStatuses.Suspended;
            hostedRunner.EnforcementState = HostedRunnerEnforcementStates.Suspended;
            hostedRunner.SuspendedAtUtc = DateTime.UtcNow;
            hostedRunner.LastEnforcementReason = "Hosted Runner container was stopped after the storage grace period.";
            deployment.Logs = "Hosted Runner container stopped; persistent volumes were preserved.";
            deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
            deployment.FinishedAt = DateTime.UtcNow;
            await WriteLifecycleAuditAsync(hostedRunner, actionKey, succeeded: true, null, cancellationToken);
            return;
        }

        if (actionKey == StorageEnforcementActions.ResumeHostedRunner)
        {
            hostedRunner.Status = HostedRunnerStatuses.Provisioning;
            hostedRunner.EnforcementState = HostedRunnerEnforcementStates.ResumePending;
            deployment.ExpectedRunnerId ??= hostedRunner.RunnerId;
            deployment.Status = HostedRunnerDeploymentStatuses.WaitingForReconnect;
            deployment.ReconnectAfterUtc ??= deployment.StartedAt;
            deployment.ReconnectDeadlineUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(hostedRunnerOptions.Value.RestartReconnectTimeoutSeconds, 30, 900));
            deployment.Logs = "Resume command completed; waiting for the hosted Runner to reconnect.";
            return;
        }

        if (actionKey == StorageTelemetryActions.MeasureHostedRunnerStorage)
        {
            RunnerStorageTelemetryService.EnsureAllowance(hostedRunner);
            var verification = await db.HostedRunnerStorageVerifications
                .FirstOrDefaultAsync(x => x.Id == deployment.Id, cancellationToken)
                ?? throw new InvalidOperationException("Storage verification request was not found.");
            var systemBytes = ReadNonNegativeInt64(result, "systemBytes");
            var userBytes = ReadNonNegativeInt64(result, "userBytes");
            var totalBytes = ReadNonNegativeInt64(result, "totalBytes");
            if (!result.TryGetProperty("usageSource", out var usageSource)
                || usageSource.ValueKind != JsonValueKind.String
                || !string.Equals(usageSource.GetString(), "xfs-project-quota", StringComparison.Ordinal))
                throw new InvalidOperationException("Storage verification did not report authoritative XFS project-quota usage.");
            if (systemBytes + userBytes != totalBytes)
                throw new InvalidOperationException("Storage verification returned inconsistent totals.");
            var now = DateTime.UtcNow;
            db.HostedRunnerStorageObservations.Add(new HostedRunnerStorageObservation
            {
                Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
                Source = StorageMeasurementSources.InfrastructureRunnerVerified,
                MeasuredAtUtc = now, ReceivedAtUtc = now,
                SystemBytes = systemBytes, UserBytes = userBytes, TotalBytes = totalBytes,
                Success = true
            });
            hostedRunner.StorageVerifiedAtUtc = now;
            hostedRunner.StorageVerifiedSystemBytes = systemBytes;
            hostedRunner.StorageVerifiedUserBytes = userBytes;
            hostedRunner.StorageVerifiedTotalBytes = totalBytes;
            hostedRunner.StorageMeasuredAtUtc = now;
            hostedRunner.StorageReceivedAtUtc = now;
            hostedRunner.StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified;
            hostedRunner.StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded;
            hostedRunner.StorageSystemBytes = systemBytes;
            hostedRunner.StorageUserBytes = userBytes;
            hostedRunner.StorageTotalBytes = totalBytes;
            hostedRunner.StorageUsagePercent = hostedRunner.StorageAllowanceBytes > 0
                ? totalBytes * 100m / hostedRunner.StorageAllowanceBytes : null;
            var newStorageState = StorageTelemetryPolicy.EvaluateHostedRunner(
                hostedRunner.StorageAllowanceBytes, totalBytes, success: true, storageTelemetryOptions.Value);
            if (!string.Equals(hostedRunner.StorageState, newStorageState, StringComparison.Ordinal))
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid(), Category = LogCategories.Audit, Action = AuditActions.Changed,
                    EntityType = AuditEntityTypes.HostedRunner, EntityId = hostedRunner.Id,
                    TargetUserId = hostedRunner.UserId, Outcome = AuditOutcomes.Success,
                    EventType = StorageTelemetryAuditEvents.HostedRunnerPolicyChanged,
                    Message = $"Hosted Runner storage policy changed from '{hostedRunner.StorageState}' to '{newStorageState}'.",
                    MetadataJson = JsonSerializer.Serialize(new { PreviousState = hostedRunner.StorageState, NewState = newStorageState }),
                    CreatedAtUtc = now
                });
            }
            hostedRunner.StorageState = newStorageState;
            hostedRunner.StorageMeasurementError = null;
            verification.Status = StorageVerificationStatuses.Succeeded;
            verification.CompletedAtUtc = now;
            verification.Error = null;
        }

        deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
        deployment.FinishedAt = DateTime.UtcNow;
        deployment.Logs = $"Trusted infrastructure Action '{actionKey}' completed.";
        if (actionKey == "delete-hosted-runner")
        {
            hostedRunner.ApiKeyId = null;
            hostedRunner.RunnerId = null;
            hostedRunner.ContainerId = null;
            hostedRunner.RuntimeEndpoint = null;
            hostedRunner.RuntimePort = null;
            hostedRunner.Status = HostedRunnerStatuses.Deleted;
            hostedRunner.DeletedAt = DateTime.UtcNow;
        }
        await WriteLifecycleAuditAsync(hostedRunner, actionKey, succeeded: true, null, cancellationToken);
    }

    private Task WriteLifecycleAuditAsync(HostedRunner runner, string actionKey, bool succeeded, string? error, CancellationToken cancellationToken)
    {
        var eventType = actionKey switch
        {
            "restart-hosted-runner" => succeeded ? AuditEventTypes.RunnerRestartSucceeded : AuditEventTypes.RunnerRestartFailed,
            StorageEnforcementActions.SuspendHostedRunner => succeeded ? StorageTelemetryAuditEvents.EnforcementSuspensionSucceeded : StorageTelemetryAuditEvents.EnforcementSuspensionFailed,
            StorageEnforcementActions.ResumeHostedRunner => succeeded ? StorageTelemetryAuditEvents.EnforcementResumeSucceeded : StorageTelemetryAuditEvents.EnforcementResumeFailed,
            "delete-hosted-runner" => succeeded ? AuditEventTypes.HostedRunnerDeleted : AuditEventTypes.HostedRunnerDeleteFailed,
            StorageTelemetryActions.MeasureHostedRunnerStorage => succeeded ? AuditEventTypes.HostedRunnerStorageVerificationSucceeded : AuditEventTypes.HostedRunnerStorageVerificationFailed,
            _ => succeeded ? AuditEventTypes.HostedRunnerProvisioningSucceeded : AuditEventTypes.HostedRunnerProvisioningFailed
        };
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

    private static long ReadNonNegativeInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || !element.TryGetInt64(out var value) || value < 0)
            throw new InvalidOperationException($"Infrastructure Action returned invalid {property}.");
        return value;
    }
}
