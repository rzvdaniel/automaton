using System.Text.Json;
using Automaton.Api.Data;
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
using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

public sealed class InfrastructureActionProvisioningHandler(
    ApplicationDbContext db,
    IHubContext<AutomatonHub> hub,
    IEventPublisher eventPublisher,
    IAuditEventService auditEventService,
    IExecutionSecretGrantService secretGrantService,
    TrustedInfrastructureActionDeploymentService actionDeploymentService,
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

            var module = await actionDeploymentService.EnsureDeployedAsync(
                infrastructureRunner.ConnectionId!, message.ActionKey, cancellationToken);

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
                    new ReadAppModuleExecutionArtifactRequest(InfrastructureAppTrust.PlatformAppId, message.ActionKey, execution.Id),
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
            hostedRunner.Status = message.ActionKey == "delete-hosted-runner"
                ? HostedRunnerStatuses.DeleteFailed
                : HostedRunnerStatuses.Failed;
            hostedRunner.LastError = sanitizedError;
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
            await WriteLifecycleAuditAsync(hostedRunner, message.ActionKey, succeeded: false, hostedRunner.LastError, CancellationToken.None);
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

    private async Task ApplySuccessfulOperationAsync(
        HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        string actionKey,
        JsonElement result,
        CancellationToken cancellationToken)
    {
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        deployment.Error = null;
        if (actionKey is "provision-hosted-runner" or "reprovision-hosted-runner")
        {
            hostedRunner.ContainerId = result.TryGetProperty("containerId", out var containerId) ? containerId.GetString() : null;
            hostedRunner.Status = HostedRunnerStatuses.Provisioning;
            deployment.Status = HostedRunnerDeploymentStatuses.WaitingForRegistration;
            deployment.Logs = WaitingForRegistrationLog;
            return;
        }

        deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
        deployment.FinishedAt = DateTime.UtcNow;
        deployment.Logs = $"Trusted infrastructure Action '{actionKey}' completed.";
        if (actionKey == "restart-hosted-runner")
        {
            hostedRunner.Status = HostedRunnerStatuses.Running;
        }
        else if (actionKey == "delete-hosted-runner")
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
            "delete-hosted-runner" => succeeded ? AuditEventTypes.HostedRunnerDeleted : AuditEventTypes.HostedRunnerDeleteFailed,
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
