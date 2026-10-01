using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class HostedRunnerDeploymentResultService(
    ApplicationDbContext db,
    HostedRunnerDeploymentCommandService commands,
    DeploymentPackageStore packageStore,
    DeploymentPackageFinalizationService finalization,
    AppDeploymentTargetStateService targetState)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumRejectionReasonLength = 1000;

    public async Task<string> RejectAsync(
        Guid apiKeyId,
        Guid operationId,
        HostedRunnerDeploymentCommandRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var runnerId = await commands.GetHostedRunnerIdAsync(apiKeyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The API key is not assigned to a Hosted Runner.");
        var operation = await db.DeploymentOperations.SingleOrDefaultAsync(
            item => item.Id == operationId, cancellationToken);
        if (operation is null)
            return "obsolete";
        var terminalOutcome = GetTerminalOutcome(operation, request.ExecutionAttemptId);
        if (terminalOutcome is not null)
            return terminalOutcome;
        if (!IsCurrentAttempt(operation, request.ExecutionAttemptId))
            return "stale";

        var target = await commands.ResolveTargetAsync(operation, cancellationToken);
        if (target?.ExecutionRunnerId != runnerId)
            throw new UnauthorizedAccessException("The deployment operation is not assigned to this Runner.");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "The Hosted Runner rejected an invalid deployment command."
            : request.Reason[..Math.Min(request.Reason.Length, MaximumRejectionReasonLength)];
        return await ApplyAsync(apiKeyId, operationId, new HostedRunnerDeploymentResultRequest(
            request.ExecutionAttemptId,
            new DeployAppPackageResult
            {
                DeploymentTargetId = target.Id,
                Success = false,
                Message = reason
            }), cancellationToken);
    }

    public async Task<string> ApplyAsync(
        Guid apiKeyId,
        Guid operationId,
        HostedRunnerDeploymentResultRequest request,
        CancellationToken cancellationToken)
    {
        var runnerId = await commands.GetHostedRunnerIdAsync(apiKeyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The API key is not assigned to a Hosted Runner.");
        var operation = await db.DeploymentOperations.SingleOrDefaultAsync(
            item => item.Id == operationId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The deployment operation was not found.");
        var terminalOutcome = GetTerminalOutcome(operation, request.ExecutionAttemptId);
        if (terminalOutcome is not null)
            return terminalOutcome;
        if (!IsCurrentAttempt(operation, request.ExecutionAttemptId))
            return "stale";

        var target = EnsureRunnerAssignment(
            await commands.ResolveTargetAsync(operation, cancellationToken), runnerId, request.Result.DeploymentTargetId);
        var response = MapResult(request.Result);
        var resultJson = JsonSerializer.Serialize(response, JsonOptions);
        if (operation.ResultJson is not null
            && !string.Equals(operation.ResultJson, resultJson, StringComparison.Ordinal))
            throw new InvalidOperationException("A different deployment result is already persisted for this attempt.");
        if (operation.ResultJson is not null && target.ActiveDeploymentOperationId != operationId)
        {
            if (!IsFinalizedTarget(target, request.Result.Success))
                return "stale";
            var recoveredError = request.Result.Success ? null : request.Result.Message;
            return await PersistResultAsync(operationId, request.ExecutionAttemptId, response,
                recoveredError, request.Result.Success, cancellationToken)
                ? "accepted"
                : "stale";
        }
        EnsureTargetAttempt(target, operationId, request.ExecutionAttemptId);
        if (!await PersistPendingResultAsync(operationId, request.ExecutionAttemptId, resultJson, cancellationToken))
            return "stale";

        return await CompleteTargetAndResultAsync(operation, target, runnerId, request.Result, response, cancellationToken);
    }

    private async Task<string> CompleteTargetAndResultAsync(DeploymentOperation operation, AppDeploymentTarget target,
        Guid runnerId, DeployAppPackageResult result, DeployPackageResponse response, CancellationToken cancellationToken)
    {
        try { await FinalizeTargetAsync(operation, target, runnerId, result, cancellationToken); }
        catch (ManagedApplicationArtifactFailureException failure)
        {
            return await FailArtifactSynchronizationAsync(operation, target, failure.Message, cancellationToken);
        }
        var error = result.Success ? null : result.Message;
        return await PersistResultAsync(operation.Id, operation.ExecutionAttemptId!.Value, response, error,
            result.Success, cancellationToken)
            ? "accepted"
            : "stale";
    }

    private async Task<bool> PersistResultAsync(
        Guid operationId,
        Guid attemptId,
        DeployPackageResponse response,
        string? error,
        bool success,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await PersistResultInMemoryAsync(operationId, attemptId, response, error, success, cancellationToken);

        var completed = await db.DeploymentOperations
            .Where(item => item.Id == operationId
                && item.Status == DeploymentOperationStatuses.Running
                && item.ExecutionAttemptId == attemptId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, success
                    ? DeploymentOperationStatuses.Succeeded
                    : DeploymentOperationStatuses.Failed)
                .SetProperty(item => item.CompletedAtUtc, DateTime.UtcNow)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(item => item.ErrorMessage, error)
                .SetProperty(item => item.ResultJson, JsonSerializer.Serialize(response, JsonOptions)),
                cancellationToken);
        return completed == 1;
    }

    private async Task<string> FailArtifactSynchronizationAsync(DeploymentOperation operation,
        AppDeploymentTarget target, string error, CancellationToken token)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token) : null;
        var response = new DeployPackageResponse
        {
            DeploymentTargetId = target.Id, Success = false, Message = error,
            Logs = [$"Host deployment completed, but managed artifact synchronization failed: {error}"]
        };
        if (!await PersistResultAsync(operation.Id, operation.ExecutionAttemptId!.Value, response, error, false, token))
            return "stale";
        // Refresh the target inside the transaction before checking/releasing its current attempt.
        await db.Entry(target).ReloadAsync(token);
        await targetState.MarkCompletedAsync(operation.WebAppId, target.Id, false, token,
            deploymentOperationId: operation.Id, executionAttemptId: operation.ExecutionAttemptId);
        if (transaction is not null) await transaction.CommitAsync(token);
        return "accepted";
    }

    private static string? GetTerminalOutcome(DeploymentOperation operation, Guid attemptId)
    {
        if (operation.Status is not (DeploymentOperationStatuses.Succeeded or DeploymentOperationStatuses.Failed))
            return null;
        return operation.ExecutionAttemptId == attemptId ? "already-applied" : "stale";
    }

    private static bool IsCurrentAttempt(DeploymentOperation operation, Guid attemptId)
        => operation.Status == DeploymentOperationStatuses.Running
            && operation.ExecutionAttemptId == attemptId;

    private static AppDeploymentTarget EnsureRunnerAssignment(
        AppDeploymentTarget? target,
        Guid runnerId,
        Guid deploymentTargetId)
    {
        if (target is not null
            && target.ExecutionRunnerId == runnerId
            && deploymentTargetId == target.Id)
            return target;
        throw new UnauthorizedAccessException("The deployment result target is not assigned to this Runner.");
    }

    private static void EnsureTargetAttempt(AppDeploymentTarget target, Guid operationId, Guid attemptId)
    {
        if (target.ActiveDeploymentOperationId == operationId
            && target.ActiveDeploymentExecutionAttemptId == attemptId)
            return;
        throw new UnauthorizedAccessException("The deployment result does not match the active target attempt.");
    }

    private static bool IsFinalizedTarget(AppDeploymentTarget target, bool succeeded)
        => target.ActiveDeploymentOperationId is null
            && target.ActiveDeploymentExecutionAttemptId is null
            && (succeeded
                ? target.Status == Automaton.Api.Enums.DeploymentTargetStatus.Running
                : target.Status == Automaton.Api.Enums.DeploymentTargetStatus.Failed);

    private async Task<bool> PersistPendingResultAsync(
        Guid operationId,
        Guid attemptId,
        string resultJson,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            var operation = await db.DeploymentOperations.SingleOrDefaultAsync(item => item.Id == operationId,
                cancellationToken);
            if (operation is null || operation.Status != DeploymentOperationStatuses.Running
                || operation.ExecutionAttemptId != attemptId
                || operation.ResultJson is not null && operation.ResultJson != resultJson)
                return false;
            operation.ResultJson = resultJson;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updated = await db.DeploymentOperations
            .Where(item => item.Id == operationId
                && item.Status == DeploymentOperationStatuses.Running
                && item.ExecutionAttemptId == attemptId
                && item.ResultJson == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ResultJson, resultJson), cancellationToken);
        return updated == 1 || await db.DeploymentOperations.AsNoTracking().AnyAsync(item =>
            item.Id == operationId && item.Status == DeploymentOperationStatuses.Running
            && item.ExecutionAttemptId == attemptId && item.ResultJson == resultJson, cancellationToken);
    }

    private async Task<bool> PersistResultInMemoryAsync(
        Guid operationId,
        Guid attemptId,
        DeployPackageResponse response,
        string? error,
        bool success,
        CancellationToken cancellationToken)
    {
        var operation = await db.DeploymentOperations.SingleOrDefaultAsync(item => item.Id == operationId,
            cancellationToken);
        if (operation is null || operation.Status != DeploymentOperationStatuses.Running
            || operation.ExecutionAttemptId != attemptId)
            return false;
        operation.Status = success ? DeploymentOperationStatuses.Succeeded : DeploymentOperationStatuses.Failed;
        operation.CompletedAtUtc = DateTime.UtcNow;
        operation.LeaseExpiresAtUtc = null;
        operation.ErrorMessage = error;
        operation.ResultJson = JsonSerializer.Serialize(response, JsonOptions);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task FinalizeTargetAsync(
        DeploymentOperation operation,
        AppDeploymentTarget target,
        Guid runnerId,
        DeployAppPackageResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            await targetState.MarkCompletedAsync(operation.WebAppId, target.Id, false,
                cancellationToken, deploymentOperationId: operation.Id,
                executionAttemptId: operation.ExecutionAttemptId);
            return;
        }

        var runner = await db.Runners.AsNoTracking().SingleAsync(item => item.Id == runnerId, cancellationToken);
        await using var stream = packageStore.OpenRead(operation.PackagePath);
        var file = new FormFile(stream, 0, stream.Length, "file", "deploy-package.zip");
        var application = await DeployPackageContractService.ReadAsync(file, operation.WebAppId, cancellationToken);
        await finalization.FinalizeAsync(operation.WebAppId, target.Id, operation.OwnerUserId,
            runner, file, application, cancellationToken, operation.Id);
        await targetState.MarkCompletedAsync(operation.WebAppId, target.Id, true,
            cancellationToken, application.Runtime.Command is not null,
            operation.Id, operation.ExecutionAttemptId);
    }

    private static DeployPackageResponse MapResult(DeployAppPackageResult result) => new()
    {
        DeploymentTargetId = result.DeploymentTargetId,
        Success = result.Success,
        RunnerCount = result.Success ? 1 : 0,
        SourceFilesDeployed = result.SourceFilesDeployed,
        RuntimeFilesDeployed = result.RuntimeFilesDeployed,
        SnapshotPath = result.SnapshotPath,
        Message = result.Message,
        Logs = result.Logs,
        Actions = result.Actions
    };
}
