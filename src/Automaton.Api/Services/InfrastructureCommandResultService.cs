using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureCommandResultService(
    ApplicationDbContext db,
    IExecutionSecretGrantService secretGrants,
    InfrastructureActionProvisioningHandler lifecycle)
{
    public async Task<InfrastructureCommandResultResponse> ApplyAsync(
        Guid commandId,
        InfrastructureCommandResultRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsTerminal(request.Execution.Status))
            throw new InvalidOperationException("Infrastructure command results must be terminal.");

        var fingerprint = Fingerprint(request);
        var deployment = await db.HostedRunnerDeployments
            .Include(operation => operation.HostedRunner)
            .SingleAsync(operation => operation.Id == request.OperationId, cancellationToken);
        var existing = await db.AppModuleExecutions
            .SingleOrDefaultAsync(execution => execution.Id == commandId, cancellationToken);
        var replayOutcome = GetTerminalReplayOutcome(existing, fingerprint);
        if (replayOutcome is not null)
            return replayOutcome;
        if (IsTerminalDeployment(deployment.Status))
            return new(InfrastructureCommandResultOutcomes.TerminalStale);

        var execution = existing ?? CreateExecution(commandId, request);
        ApplyExecution(execution, request.Execution);
        execution.ResultFingerprint = fingerprint;
        if (existing is null)
            db.AppModuleExecutions.Add(execution);

        await ApplyDeploymentResultAsync(deployment, request, execution, cancellationToken);
        if (request.SecretGrantIds.Count > 0)
            await secretGrants.MarkConsumedAsync(request.SecretGrantIds, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new(InfrastructureCommandResultOutcomes.Accepted);
    }

    private async Task ApplyDeploymentResultAsync(
        HostedRunnerDeployment deployment,
        InfrastructureCommandResultRequest request,
        AppModuleExecution execution,
        CancellationToken cancellationToken)
    {
        if (request.Execution.Status == AppModuleExecutionStatuses.Succeeded)
        {
            await ApplySuccessfulDeploymentResultAsync(deployment, request, cancellationToken);
            return;
        }

        await ApplyFailedDeploymentResultAsync(deployment, request, execution, cancellationToken);
    }

    private async Task ApplySuccessfulDeploymentResultAsync(
        HostedRunnerDeployment deployment,
        InfrastructureCommandResultRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Result.Available || string.IsNullOrWhiteSpace(request.Result.Content))
            throw new InvalidOperationException("A successful infrastructure command requires a result artifact.");
        using var document = JsonDocument.Parse(request.Result.Content);
        if (!document.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new InvalidOperationException("Infrastructure Action reported an unsuccessful host mutation.");

        await lifecycle.ApplySuccessfulOperationAsync(
            deployment.HostedRunner,
            deployment,
            request.ActionKey,
            document.RootElement,
            cancellationToken);
    }

    private async Task ApplyFailedDeploymentResultAsync(
        HostedRunnerDeployment deployment,
        InfrastructureCommandResultRequest request,
        AppModuleExecution execution,
        CancellationToken cancellationToken)
    {
        deployment.Status = HostedRunnerDeploymentStatuses.Failed;
        deployment.Error = execution.FailureSummary ?? execution.ErrorCode ?? "Infrastructure Action failed.";
        deployment.FinishedAt = DateTime.UtcNow;
        deployment.HostedRunner.UpdatedAt = deployment.FinishedAt;

        if (request.ActionKey == StorageTelemetryActions.MeasureHostedRunnerStorage)
        {
            await ApplyFailedStorageMeasurementAsync(deployment, cancellationToken);
            return;
        }

        deployment.HostedRunner.Status = request.ActionKey == "delete-hosted-runner"
            ? HostedRunnerStatuses.DeleteFailed
            : HostedRunnerStatuses.Failed;
        deployment.HostedRunner.LastError = deployment.Error;
    }

    private async Task ApplyFailedStorageMeasurementAsync(
        HostedRunnerDeployment deployment,
        CancellationToken cancellationToken)
    {
        var verification = await db.HostedRunnerStorageVerifications
            .SingleOrDefaultAsync(request => request.Id == deployment.Id, cancellationToken)
            ?? throw new InvalidOperationException("Storage verification request was not found.");
        verification.Status = StorageVerificationStatuses.Failed;
        verification.CompletedAtUtc = deployment.FinishedAt;
        verification.Error = Truncate(deployment.Error, 2000);
        deployment.HostedRunner.StorageMeasurementStatus = StorageMeasurementStatuses.Failed;
        deployment.HostedRunner.StorageMeasurementError = verification.Error;
        deployment.HostedRunner.StorageState = HostedRunnerStorageStates.MeasurementFailed;
    }

    private static InfrastructureCommandResultResponse? GetTerminalReplayOutcome(
        AppModuleExecution? existing,
        string fingerprint)
    {
        if (existing is null || !IsTerminal(existing.Status))
            return null;

        var outcome = string.Equals(existing.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            ? InfrastructureCommandResultOutcomes.AlreadyApplied
            : InfrastructureCommandResultOutcomes.TerminalStale;
        return new InfrastructureCommandResultResponse(outcome);
    }

    private static AppModuleExecution CreateExecution(Guid commandId, InfrastructureCommandResultRequest request) => new()
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
        var payload = JsonSerializer.Serialize(new
        {
            request.OperationId,
            request.RunnerHostId,
            request.InfrastructureRunnerId,
            request.ActionKey,
            request.Execution,
            request.Result.Content
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
