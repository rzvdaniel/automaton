using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using Automaton.Api.Logging;

namespace Automaton.Api.Services;

public sealed class InfrastructureActionProvisioningHandler(
    ApplicationDbContext db,
    IHubContext<AutomatonHub> hub,
    IEventPublisher eventPublisher,
    IAuditEventService auditEventService,
    IExecutionSecretGrantService secretGrantService,
    ITrustedInfrastructureActionDeploymentService actionDeploymentService,
    IOptions<HostedRunnerOptions> hostedRunnerOptions,
    IOptions<StorageTelemetryOptions> storageTelemetryOptions,
    ILogger<InfrastructureActionProvisioningHandler> logger)
{
    private const string WaitingForRegistrationLog = "Infrastructure Action completed; waiting for the hosted Runner to register.";

    public async Task HandleAsync(InfrastructureActionExecutionRequestedEvent message, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.Id == message.HostedRunnerId && x.DeletedAt == null, cancellationToken);
        var host = await db.RunnerHosts
            .Include(x => x.InfrastructureRunner)
            .FirstOrDefaultAsync(x => x.Id == message.RunnerHostId, cancellationToken);
        var deployment = hostedRunner?.Deployments.FirstOrDefault(x => x.Id == message.OperationId);
        var storageVerification = message.ActionKey == StorageTelemetryActions.MeasureHostedRunnerStorage
            ? await db.HostedRunnerStorageVerifications.FirstOrDefaultAsync(x => x.Id == message.OperationId, cancellationToken)
            : null;

        if (hostedRunner == null || deployment == null)
        {
            logger.LogWarning("Infrastructure Action request {OperationId} no longer has a provisioning operation", message.OperationId);
            return;
        }

        var execution = await db.AppModuleExecutions
            .FirstOrDefaultAsync(x => x.HostedRunnerOperationId == message.OperationId, cancellationToken);
        if (execution != null && IsTerminalExecution(execution.Status))
        {
            logger.LogInformation(
                "Reconciling terminal infrastructure Action operation {OperationId} from execution {ExecutionId} with status {Status}",
                message.OperationId,
                execution.Id,
                execution.Status);
        }

        if (deployment.Status is HostedRunnerDeploymentStatuses.Succeeded
            or HostedRunnerDeploymentStatuses.Failed
            or HostedRunnerDeploymentStatuses.Skipped
            or HostedRunnerDeploymentStatuses.WaitingForReconnect
            || (message.ActionKey is "provision-hosted-runner" or "reprovision-hosted-runner")
                && string.Equals(deployment.Logs, WaitingForRegistrationLog, StringComparison.Ordinal))
        {
            logger.LogInformation("Ignoring duplicate infrastructure Action request for terminal operation {OperationId}", message.OperationId);
            return;
        }
        var secretSanitizer = SecretValueSanitizer.Empty;
        try
        {
            if (hostedRunner.UserId != message.UserId || hostedRunner.RunnerHostId != message.RunnerHostId)
                throw new UnauthorizedAccessException("The infrastructure Action request does not match the hosted Runner assignment.");

            var infrastructureRunner = host?.InfrastructureRunner
                ?? throw new InvalidOperationException("The selected Runner Host has no Infrastructure Runner assigned.");
            if (host!.Status != RunnerHostStatuses.Active
                || host.ProvisioningStatus != RunnerHostProvisioningStatuses.Ready
                || !host.ValidatedAtUtc.HasValue
                || !host.ApprovedAtUtc.HasValue)
                throw new UnauthorizedAccessException("The selected Runner Host is not active, validated, approved, and ready.");
            if (!string.Equals(infrastructureRunner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The selected Runner is not an Infrastructure Runner.");
            if (!RunnerCapabilities.Includes(infrastructureRunner.CapabilitiesJson, RunnerCapabilities.HostedRunnerProvisioning, infrastructureRunner.Purpose))
                throw new UnauthorizedAccessException("The Infrastructure Runner does not allow hosted Runner provisioning.");
            if (!RunnerPresenceEvaluator.IsOnline(infrastructureRunner.ConnectionId, infrastructureRunner.LastHeartbeatAtUtc, DateTime.UtcNow))
                throw new InvalidOperationException("The Infrastructure Runner is not online.");

            AppActionModuleInfo module;
            try
            {
                module = await actionDeploymentService.EnsureDeployedAsync(
                    infrastructureRunner.ConnectionId!, message.ActionKey, cancellationToken);
            }
            catch (Exception ex) when (IsMissingSignalRConnection(ex))
            {
                // No execution has been claimed yet. Let CAP retry this delivery after
                // the Runner reconnects instead of turning a stale connection into a
                // terminal provisioning failure. Never switch connections in-flight.
                db.ChangeTracker.Clear();
                throw new InfrastructureActionConnectionUnavailableException(
                    message.OperationId,
                    infrastructureRunner.ConnectionId!,
                    ex);
            }
            var deploymentTargetId = await db.AppDeploymentTargets
                .Where(target =>
                    target.WebAppId == InfrastructureAppTrust.PlatformAppId &&
                    target.IsDefault)
                .Select(target => target.Id)
                .SingleAsync(cancellationToken);

            if (message.ActionKey is "provision-hosted-runner" or "reprovision-hosted-runner"
                && message.SecretGrantIds.Count == 0)
                throw new UnauthorizedAccessException("The infrastructure Action has no enrollment credential grant.");

            using var inputDocument = JsonDocument.Parse(message.InputJson);
            var now = DateTime.UtcNow;
            if (execution == null)
            {
                execution = new AppModuleExecution
                {
                    Id = Guid.NewGuid(),
                    HostedRunnerOperationId = message.OperationId,
                    AppId = InfrastructureAppTrust.PlatformAppId,
                    DeploymentTargetId = deploymentTargetId,
                    ModuleType = AppModuleTypes.Action,
                    ModuleKey = message.ActionKey,
                    RunnerId = infrastructureRunner.Id,
                    TriggerType = AppModuleTriggerTypes.Internal,
                    Status = AppModuleExecutionStatuses.Dispatching,
                    DeploymentHash = module.ContentHash,
                    CreatedAtUtc = now,
                    StartedAtUtc = now,
                    ExecutionDeadlineUtc = now.AddSeconds(module.TimeoutSeconds + 300),
                    CorrelationId = $"hosted-runner-operation:{message.OperationId:N}"
                };
                if (!await TryClaimAndCreateExecutionAsync(deployment, execution, cancellationToken))
                {
                    logger.LogInformation("Ignoring duplicate infrastructure Action request for claimed operation {OperationId} in state {Status}", message.OperationId, deployment.Status);
                    return;
                }
                if (storageVerification != null)
                {
                    storageVerification.Status = StorageVerificationStatuses.Running;
                    storageVerification.StartedAtUtc = now;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                logger.LogWarning(
                    "Retrying interrupted infrastructure Action dispatch for operation {OperationId} with execution {ExecutionId} in state {Status}",
                    message.OperationId,
                    execution.Id,
                    execution.Status);
            }

            ExecutionSecretLease? secretLease = null;
            if (!IsTerminalExecution(execution.Status) && message.SecretGrantIds.Count > 0)
            {
                secretLease = await secretGrantService.LeaseAsync(
                    new SecretGrantScope(
                        message.OperationId,
                        InfrastructureAppTrust.PlatformAppId,
                        message.ActionKey,
                        infrastructureRunner.Id,
                        host.Id,
                        SecretOwnerTypes.HostedRunner,
                        hostedRunner.Id),
                    message.SecretGrantIds,
                    cancellationToken);
                secretSanitizer = new SecretValueSanitizer(secretLease.EnvironmentVariables.Values);
            }

            var result = IsTerminalExecution(execution.Status)
                ? ToExecutionResult(execution)
                : await hub.Clients.Client(infrastructureRunner.ConnectionId!)
                    .InvokeAsync<AppActionExecutionResult>(
                        AutomatonHubMethods.ExecuteAppActionModule,
                        new ExecuteAppActionModuleRequest
                        {
                            ExecutionId = execution.Id,
                            AppId = InfrastructureAppTrust.PlatformAppId,
                            DeploymentTargetId = deploymentTargetId,
                            ActionKey = message.ActionKey,
                            ExpectedDeploymentHash = module.ContentHash,
                            TimeoutSeconds = module.TimeoutSeconds,
                            SystemInfrastructure = true,
                            Input = inputDocument.RootElement.Clone(),
                            Secrets = secretLease == null
                                ? []
                                : new Dictionary<string, string>(secretLease.EnvironmentVariables, StringComparer.Ordinal)
                        },
                        cancellationToken);

            if (secretLease != null)
            {
                await secretGrantService.MarkConsumedAsync(secretLease.GrantIds, cancellationToken);
                await auditEventService.WriteAsync(new AuditEventWriteRequest
                {
                    Action = AuditActions.Succeeded,
                    EntityType = AuditEntityTypes.ExecutionSecretGrant,
                    EntityId = secretLease.GrantIds.First(),
                    EventType = AuditEventTypes.ExecutionSecretGrantDelivered,
                    Outcome = AuditOutcomes.Success,
                    Message = "Execution-scoped credentials were delivered to an Infrastructure Runner.",
                    TargetUserId = hostedRunner.UserId,
                    Metadata = new
                    {
                        message.OperationId,
                        AppId = InfrastructureAppTrust.PlatformAppId,
                        message.ActionKey,
                        RunnerId = infrastructureRunner.Id,
                        RunnerHostId = host.Id,
                        GrantIds = secretLease.GrantIds
                    }
                }, cancellationToken);
            }

            ApplyExecutionResult(execution, result, secretSanitizer);
            if (!string.Equals(result.Status, AppModuleExecutionStatuses.Succeeded, StringComparison.Ordinal))
                throw new InvalidOperationException(execution.FailureSummary ?? result.ErrorCode ?? "Infrastructure Action failed.");

            var artifact = await hub.Clients.Client(infrastructureRunner.ConnectionId!)
                .InvokeAsync<AppModuleExecutionArtifactResponse>(
                    AutomatonHubMethods.ReadAppModuleExecutionResult,
                    new ReadAppModuleExecutionArtifactRequest(
                        InfrastructureAppTrust.PlatformAppId,
                        deploymentTargetId,
                        message.ActionKey,
                        execution.Id),
                    cancellationToken);
            if (!artifact.Available || string.IsNullOrWhiteSpace(artifact.Content))
                throw new InvalidOperationException("Infrastructure Action completed without a result artifact.");
            using var resultDocument = JsonDocument.Parse(secretSanitizer.Sanitize(artifact.Content));
            var root = resultDocument.RootElement;
            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
                throw new InvalidOperationException("Infrastructure Action reported an unsuccessful host mutation.");

            await ApplySuccessfulOperationAsync(hostedRunner, deployment, message.ActionKey, root, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (LifecycleClaimPersistenceException ex)
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(
                "Infrastructure Action claim persistence failed for operation {OperationId}; CAP will retry. {ExceptionType}",
                message.OperationId,
                ex.InnerException?.GetType().Name ?? ex.GetType().Name);
            throw;
        }
        catch (InfrastructureActionConnectionUnavailableException)
        {
            // CAP must redeliver after the Runner reconnects. The operation has no
            // execution claim yet, so do not persist a terminal lifecycle outcome.
            throw;
        }
        catch (Exception ex)
        {
            var sanitizedError = Limit(
                RedactCredentialPatterns(secretSanitizer.Sanitize(ex.Message)),
                2000);
            logger.LogError(
                "Infrastructure Action provisioning failed for hosted Runner {HostedRunnerId}. {ExceptionType} at {ExceptionLocation}: {FailureSummary}",
                hostedRunner.Id,
                ex.GetType().Name,
                $"{ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name}",
                sanitizedError);
            if (storageVerification != null)
            {
                var now = DateTime.UtcNow;
                storageVerification.Status = StorageVerificationStatuses.Failed;
                storageVerification.Error = sanitizedError;
                storageVerification.CompletedAtUtc = now;
                db.HostedRunnerStorageObservations.Add(new HostedRunnerStorageObservation
                {
                    Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
                    Source = StorageMeasurementSources.InfrastructureRunnerVerified,
                    MeasuredAtUtc = now, ReceivedAtUtc = now,
                    Success = false, Error = sanitizedError
                });
                if (!hostedRunner.StorageVerifiedAtUtc.HasValue ||
                    hostedRunner.StorageVerifiedAtUtc.Value < now.AddMinutes(-storageTelemetryOptions.Value.AuthoritativeValidityMinutes))
                {
                    if (hostedRunner.StorageState != HostedRunnerStorageStates.MeasurementFailed)
                    {
                        db.AuditEvents.Add(new AuditEvent
                        {
                            Id = Guid.NewGuid(), Category = LogCategories.Audit, Action = AuditActions.Changed,
                            EntityType = AuditEntityTypes.HostedRunner, EntityId = hostedRunner.Id,
                            TargetUserId = hostedRunner.UserId, Outcome = AuditOutcomes.Success,
                            EventType = StorageTelemetryAuditEvents.HostedRunnerPolicyChanged,
                            Message = $"Hosted Runner storage policy changed from '{hostedRunner.StorageState}' to '{HostedRunnerStorageStates.MeasurementFailed}'.",
                            MetadataJson = JsonSerializer.Serialize(new { PreviousState = hostedRunner.StorageState, NewState = HostedRunnerStorageStates.MeasurementFailed }),
                            CreatedAtUtc = now
                        });
                    }
                    hostedRunner.StorageMeasuredAtUtc = now;
                    hostedRunner.StorageReceivedAtUtc = now;
                    hostedRunner.StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified;
                    hostedRunner.StorageMeasurementStatus = StorageMeasurementStatuses.Failed;
                    hostedRunner.StorageState = HostedRunnerStorageStates.MeasurementFailed;
                    hostedRunner.StorageMeasurementError = sanitizedError;
                }
            }
            else
            {
                hostedRunner.Status = message.ActionKey == "delete-hosted-runner"
                    ? HostedRunnerStatuses.DeleteFailed
                    : HostedRunnerStatuses.Failed;
                if (message.ActionKey == StorageEnforcementActions.SuspendHostedRunner)
                    hostedRunner.EnforcementState = HostedRunnerEnforcementStates.GracePeriod;
                else if (message.ActionKey == StorageEnforcementActions.ResumeHostedRunner)
                {
                    hostedRunner.Status = HostedRunnerStatuses.Suspended;
                    hostedRunner.EnforcementState = HostedRunnerEnforcementStates.Suspended;
                }
                hostedRunner.LastError = sanitizedError;
                if (message.ActionKey is StorageEnforcementActions.SuspendHostedRunner or StorageEnforcementActions.ResumeHostedRunner)
                    hostedRunner.LastEnforcementReason = sanitizedError;
            }
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            deployment.Status = HostedRunnerDeploymentStatuses.Failed;
            deployment.Error = hostedRunner.LastError;
            deployment.FinishedAt = DateTime.UtcNow;
            if (execution != null && !IsTerminalExecution(execution.Status))
            {
                execution.Status = AppModuleExecutionStatuses.Lost;
                execution.ErrorCode = AppModuleExecutionErrorCodes.AmbiguousDispatch;
                execution.FailureSummary = hostedRunner.LastError;
                execution.CompletedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(CancellationToken.None);
            await WriteLifecycleAuditAsync(
                hostedRunner,
                message.ActionKey,
                succeeded: false,
                storageVerification != null ? sanitizedError : hostedRunner.LastError,
                CancellationToken.None);
            if (message.ActionKey is "provision-hosted-runner" or "reprovision-hosted-runner")
            {
                await eventPublisher.PublishAsync(new ManagedRunnerProvisionFailedEvent
                {
                    HostedRunnerId = hostedRunner.Id,
                    UserId = hostedRunner.UserId,
                    Error = hostedRunner.LastError,
                    CorrelationId = message.CorrelationId
                }, CancellationToken.None);
            }
        }
    }

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
            deployment.Logs = WaitingForRegistrationLog;
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
            var totalBytes = checked(systemBytes + userBytes);
            if (ReadNonNegativeInt64(result, "totalBytes") != totalBytes)
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

    private static void ApplyExecutionResult(
        AppModuleExecution execution,
        AppActionExecutionResult result,
        SecretValueSanitizer sanitizer)
    {
        execution.Status = result.Status;
        execution.ExitCode = result.ExitCode;
        execution.ErrorCode = sanitizer.SanitizeNullable(result.ErrorCode);
        execution.FailureSummary = sanitizer.SanitizeNullable(result.FailureSummary);
        execution.DeploymentHash = result.DeploymentHash ?? execution.DeploymentHash;
        execution.StartedAtUtc = result.StartedAtUtc;
        execution.CompletedAtUtc = result.CompletedAtUtc;
        execution.LogsAvailableUntilUtc = result.LogsExpireAtUtc;
        execution.ResultAvailableUntilUtc = result.ResultExpireAtUtc;
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    internal static bool IsMissingSignalRConnection(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("Connection '", StringComparison.OrdinalIgnoreCase)
                && current.Message.Contains("' does not exist", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsTerminalExecution(string status) => status is
        AppModuleExecutionStatuses.Succeeded or
        AppModuleExecutionStatuses.Failed or
        AppModuleExecutionStatuses.TimedOut or
        AppModuleExecutionStatuses.Cancelled or
        AppModuleExecutionStatuses.RunnerUnavailable or
        AppModuleExecutionStatuses.Lost;

    private static AppActionExecutionResult ToExecutionResult(AppModuleExecution execution) => new()
    {
        ExecutionId = execution.Id,
        Status = execution.Status,
        ExitCode = execution.ExitCode,
        ErrorCode = execution.ErrorCode,
        FailureSummary = execution.FailureSummary,
        DeploymentHash = execution.DeploymentHash,
        StartedAtUtc = execution.StartedAtUtc ?? execution.CreatedAtUtc,
        CompletedAtUtc = execution.CompletedAtUtc ?? DateTime.UtcNow,
        LogsExpireAtUtc = execution.LogsAvailableUntilUtc,
        ResultExpireAtUtc = execution.ResultAvailableUntilUtc,
        LogsAvailable = execution.LogsAvailableUntilUtc > DateTime.UtcNow,
        ResultAvailable = execution.ResultAvailableUntilUtc > DateTime.UtcNow
    };

    internal async Task<bool> TryClaimAndCreateExecutionAsync(
        HostedRunnerDeployment deployment,
        AppModuleExecution execution,
        CancellationToken cancellationToken)
    {
        var claimable = new[]
        {
            HostedRunnerDeploymentStatuses.Queued,
            HostedRunnerDeploymentStatuses.Dispatching,
            HostedRunnerDeploymentStatuses.Running // Compatibility with operations queued before this rollout.
        };

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            if (db.Database.IsRelational())
            {
                var affected = await db.HostedRunnerDeployments
                    .Where(x => x.Id == deployment.Id && claimable.Contains(x.Status))
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.Status, HostedRunnerDeploymentStatuses.Executing),
                        cancellationToken);
                if (affected != 1)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                    return false;
                }
            }
            else if (!claimable.Contains(deployment.Status, StringComparer.Ordinal))
            {
                return false;
            }

            deployment.Status = HostedRunnerDeploymentStatuses.Executing;
            db.AppModuleExecutions.Add(execution);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
            throw new LifecycleClaimPersistenceException(
                "The hosted Runner lifecycle claim and execution record could not be committed.",
                ex);
        }
    }

    private static string RedactCredentialPatterns(string value)
        => Regex.Replace(
            value ?? string.Empty,
            @"(?:ak|re)_[A-Za-z0-9_-]+",
            "[REDACTED]",
            RegexOptions.CultureInvariant);
}

internal sealed class LifecycleClaimPersistenceException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
