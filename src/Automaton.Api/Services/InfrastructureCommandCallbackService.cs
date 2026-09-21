using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

public sealed class InfrastructureCommandCallbackService(
    ApplicationDbContext db,
    IExecutionSecretGrantService secretGrants,
    InfrastructureActionProvisioningHandler lifecycle,
    ITransactionalEventExecutor? transactionalEventExecutor = null,
    ILogger<InfrastructureCommandCallbackService>? logger = null)
{
    public async Task<InfrastructureCommandSecretsResponse> ResolveSecretsAsync(Guid apiKeyId,
        Guid commandId, InfrastructureCommandSecretsRequest request, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(apiKeyId, commandId, request.OperationId, request.RunnerHostId,
            request.InfrastructureRunnerId, request.ActionKey, cancellationToken);
        var deployment = await db.HostedRunnerDeployments.AsNoTracking()
            .SingleAsync(x => x.Id == request.OperationId, cancellationToken);
        if (IsTerminalDeployment(deployment.Status))
            return TerminalSecretResolution(deployment.Error ?? HostedRunnerDeploymentErrors.AmbiguousDispatch);

        try
        {
            var lease = await secretGrants.LeaseForInfrastructureCommandAsync(scope, request.SecretGrantIds, cancellationToken);
            return new(InfrastructureCommandSecretResolutionDispositions.Executable, lease.EnvironmentVariables);
        }
        catch (SecretGrantUnavailableException exception)
        {
            await TerminalizeAsync(request.OperationId, request.ActionKey, exception.Reason, cancellationToken);
            logger?.LogWarning(
                "Infrastructure command {CommandId} for operation {OperationId} ({ActionKey}) was terminalized during secret resolution: {DispositionReason}; ambiguous={Ambiguous}",
                commandId, request.OperationId, request.ActionKey, exception.Reason, exception.Ambiguous);
            return TerminalSecretResolution(exception.Reason);
        }
    }

    private async Task TerminalizeAsync(Guid operationId, string actionKey, string reason, CancellationToken cancellationToken)
    {
        if (transactionalEventExecutor != null)
        {
            await transactionalEventExecutor.ExecuteAsync(
                token => lifecycle.TerminalizeUnexecutableAsync(operationId, actionKey, reason, token), cancellationToken);
            return;
        }

        await lifecycle.TerminalizeUnexecutableAsync(operationId, actionKey, reason, cancellationToken);
    }

    private static InfrastructureCommandSecretsResponse TerminalSecretResolution(string reason)
        => new(InfrastructureCommandSecretResolutionDispositions.Terminal, new Dictionary<string, string>(), reason);

    public async Task<InfrastructureCommandResultResponse> ApplyResultAsync(Guid apiKeyId,
        Guid commandId, InfrastructureCommandResultRequest request, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(apiKeyId, commandId, request.OperationId, request.RunnerHostId,
            request.InfrastructureRunnerId, request.ActionKey, cancellationToken);
        if (!IsTerminal(request.Execution.Status))
            throw new InvalidOperationException("Infrastructure command results must be terminal.");
        var fingerprint = Fingerprint(request);
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner)
            .SingleAsync(x => x.Id == request.OperationId, cancellationToken);
        var existing = await db.AppModuleExecutions.SingleOrDefaultAsync(x => x.Id == commandId, cancellationToken);
        if (existing != null && IsTerminal(existing.Status))
        {
            if (string.Equals(existing.ResultFingerprint, fingerprint, StringComparison.Ordinal))
                return new(InfrastructureCommandResultOutcomes.AlreadyApplied);
            return new(InfrastructureCommandResultOutcomes.TerminalStale);
        }
        if (IsTerminalDeployment(deployment.Status))
            return new(InfrastructureCommandResultOutcomes.TerminalStale);
        var execution = existing ?? new AppModuleExecution
        {
            Id = commandId,
            HostedRunnerOperationId = request.OperationId,
            AppId = InfrastructureAppTrust.PlatformAppId,
            ModuleType = AppModuleTypes.Action,
            ModuleKey = request.ActionKey,
            RunnerId = request.InfrastructureRunnerId,
            TriggerType = AppModuleTriggerTypes.Internal,
            CreatedAtUtc = DateTime.UtcNow,
            CorrelationId = $"hosted-runner-operation:{request.OperationId:N}"
        };
        ApplyExecution(execution, request.Execution);
        execution.ResultFingerprint = fingerprint;
        if (existing == null) db.AppModuleExecutions.Add(execution);

        if (request.Execution.Status == AppModuleExecutionStatuses.Succeeded)
        {
            if (!request.Result.Available || string.IsNullOrWhiteSpace(request.Result.Content))
                throw new InvalidOperationException("A successful infrastructure command requires a result artifact.");
            using var document = JsonDocument.Parse(request.Result.Content);
            if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
                throw new InvalidOperationException("Infrastructure Action reported an unsuccessful host mutation.");
            await lifecycle.ApplySuccessfulOperationAsync(deployment.HostedRunner, deployment, request.ActionKey,
                document.RootElement, cancellationToken);
        }
        else
        {
            deployment.Status = HostedRunnerDeploymentStatuses.Failed;
            deployment.Error = execution.FailureSummary ?? execution.ErrorCode ?? "Infrastructure Action failed.";
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.HostedRunner.Status = request.ActionKey == "delete-hosted-runner"
                ? HostedRunnerStatuses.DeleteFailed : HostedRunnerStatuses.Failed;
            deployment.HostedRunner.LastError = deployment.Error;
            deployment.HostedRunner.UpdatedAt = DateTime.UtcNow;
        }
        if (request.SecretGrantIds.Count > 0)
            await secretGrants.MarkConsumedAsync(request.SecretGrantIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new(InfrastructureCommandResultOutcomes.Accepted);
    }

    private async Task<SecretGrantScope> AuthorizeAsync(Guid apiKeyId, Guid commandId, Guid operationId,
        Guid runnerHostId, Guid runnerId, string actionKey, CancellationToken cancellationToken)
    {
        if (commandId != operationId) throw new UnauthorizedAccessException("Infrastructure command identity is invalid.");
        TrustedInfrastructureActions.EnsureAllowed(actionKey);
        var host = await db.RunnerHosts.Include(x => x.InfrastructureRunner)
            .SingleOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The Infrastructure Runner Host was not found.");
        var runner = host.InfrastructureRunner;
        if (runner == null || runner.Id != runnerId || runner.ApiKeyId != apiKeyId
            || !string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The authenticated API key does not belong to the addressed Infrastructure Runner.");
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner)
            .SingleOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The infrastructure command operation was not found.");
        if (deployment.HostedRunner.RunnerHostId != runnerHostId)
            throw new UnauthorizedAccessException("The infrastructure command operation is not assigned to this Runner Host.");
        var expectedActionKey = TrustedActionKeyForDeployment(deployment.Action)
            ?? throw new UnauthorizedAccessException("The infrastructure command operation has an unknown deployment action.");
        if (!string.Equals(expectedActionKey, actionKey, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The infrastructure command Action does not match the persisted operation action.");
        return new SecretGrantScope(operationId, InfrastructureAppTrust.PlatformAppId, actionKey, runnerId,
            runnerHostId, SecretOwnerTypes.HostedRunner, deployment.HostedRunnerId);
    }

    internal static string? TrustedActionKeyForDeployment(string action) => action switch
    {
        HostedRunnerDeploymentActions.Provision => "provision-hosted-runner",
        HostedRunnerDeploymentActions.Reprovision => "reprovision-hosted-runner",
        HostedRunnerDeploymentActions.Restart => "restart-hosted-runner",
        HostedRunnerDeploymentActions.Suspend => StorageEnforcementActions.SuspendHostedRunner,
        HostedRunnerDeploymentActions.Resume => StorageEnforcementActions.ResumeHostedRunner,
        HostedRunnerDeploymentActions.Deprovision => "delete-hosted-runner",
        HostedRunnerDeploymentActions.VerifyStorage => StorageTelemetryActions.MeasureHostedRunnerStorage,
        "measure-hosted-runner-storage" => StorageTelemetryActions.MeasureHostedRunnerStorage,
        _ => null
    };

    private static void ApplyExecution(AppModuleExecution target, AppActionExecutionResult source)
    {
        target.Status = source.Status;
        target.ExitCode = source.ExitCode;
        target.ErrorCode = source.ErrorCode;
        target.FailureSummary = source.FailureSummary;
        target.DeploymentHash = source.DeploymentHash;
        target.StartedAtUtc = source.StartedAtUtc;
        target.CompletedAtUtc = source.CompletedAtUtc;
        target.LogsAvailableUntilUtc = source.LogsExpireAtUtc;
        target.ResultAvailableUntilUtc = source.ResultExpireAtUtc;
    }

    private static bool IsTerminal(string status) => status is AppModuleExecutionStatuses.Succeeded
        or AppModuleExecutionStatuses.Failed or AppModuleExecutionStatuses.TimedOut
        or AppModuleExecutionStatuses.Cancelled or AppModuleExecutionStatuses.RunnerUnavailable
        or AppModuleExecutionStatuses.Lost;

    private static bool IsTerminalDeployment(string status) => status is
        HostedRunnerDeploymentStatuses.Succeeded or HostedRunnerDeploymentStatuses.Failed
        or HostedRunnerDeploymentStatuses.Skipped;

    private static string Fingerprint(InfrastructureCommandResultRequest request)
    {
        var payload = JsonSerializer.Serialize(new { request.OperationId, request.RunnerHostId,
            request.InfrastructureRunnerId, request.ActionKey, request.Execution, request.Result.Content });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

public sealed record InfrastructureCommandSecretsRequest(Guid OperationId, Guid RunnerHostId,
    Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds);
public sealed record InfrastructureCommandResultRequest(Guid OperationId, Guid RunnerHostId,
    Guid InfrastructureRunnerId, string ActionKey, IReadOnlyList<Guid> SecretGrantIds,
    AppActionExecutionResult Execution, AppModuleExecutionArtifactResponse Result);
