using System.Data;
using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureHostValidationResultService(
    ApplicationDbContext db,
    IAuditEventService audit)
{
    public async Task<InfrastructureCommandResultResponse> ApplyAsync(
        Guid apiKeyId, Guid commandId, InfrastructureCommandResultRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var operation = await db.InfrastructureWorkflowExecutions
            .SingleOrDefaultAsync(item => item.Id == commandId, cancellationToken)
            ?? throw new UnauthorizedAccessException("The validation operation was not found.");
        var host = await AuthorizeAsync(apiKeyId, commandId, request, operation, cancellationToken);
        if (operation.CompletedAtUtc.HasValue)
            return new(InfrastructureCommandResultOutcomes.AlreadyApplied);
        ValidateResult(commandId, request);

        ApplyExecution(operation, request);
        db.InfrastructureWorkflowExecutionLogs.AddRange(operation.Logs);
        var newer = await db.InfrastructureWorkflowExecutions.AnyAsync(item =>
            item.TargetRunnerHostId == host.Id && item.CreatedAtUtc > operation.CreatedAtUtc,
            cancellationToken);
        if (!newer)
            await ApplyHostAsync(host, operation, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(operation, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return new(InfrastructureCommandResultOutcomes.Accepted);
    }

    private Task WriteAuditAsync(InfrastructureWorkflowExecution operation, CancellationToken cancellationToken)
        => audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created, EntityType = AuditEntityTypes.Runner,
            EntityId = operation.TargetRunnerId, EventType = AuditEventTypes.InfrastructureWorkflowExecuted,
            ActorUserId = operation.RequestedByUserId,
            Message = $"Infrastructure workflow '{operation.WorkflowVersionId}' completed with status '{operation.Status}'.",
            Outcome = operation.Status == InfrastructureWorkflowExecutionStatuses.Succeeded
                ? AuditOutcomes.Success : AuditOutcomes.Failed,
            Metadata = new { operation.Id, operation.TargetRunnerHostId, operation.Status }
        }, cancellationToken);

    private async Task<RunnerHost> AuthorizeAsync(Guid apiKeyId, Guid commandId,
        InfrastructureCommandResultRequest request, InfrastructureWorkflowExecution operation,
        CancellationToken cancellationToken)
    {
        var host = await db.RunnerHosts.Include(item => item.InfrastructureRunner)
            .SingleOrDefaultAsync(item => item.Id == operation.TargetRunnerHostId, cancellationToken);
        var runner = host?.InfrastructureRunner;
        if (commandId != request.OperationId || request.ActionKey != HostValidationCommand.ActionKey
            || operation.WorkflowVersionId != HostValidationCommand.WorkflowVersion
            || request.RunnerHostId != operation.TargetRunnerHostId
            || request.InfrastructureRunnerId != operation.TargetRunnerId
            || runner is null || runner.Id != operation.TargetRunnerId
            || runner.Purpose != RunnerPurposes.Infrastructure || runner.ApiKeyId != apiKeyId)
            throw new UnauthorizedAccessException("The validation result is not assigned to this Infrastructure Runner.");
        return host!;
    }

    private static void ValidateResult(Guid commandId, InfrastructureCommandResultRequest request)
    {
        if (request.SecretGrantIds.Count != 0 || request.Execution.ExecutionId != commandId
            || request.Execution.Status is not (AppModuleExecutionStatuses.Succeeded
                or AppModuleExecutionStatuses.Failed or AppModuleExecutionStatuses.TimedOut
                or AppModuleExecutionStatuses.Cancelled or AppModuleExecutionStatuses.Lost
                or AppModuleExecutionStatuses.RunnerUnavailable))
            throw new ArgumentException("Host validation requires a terminal result for its command and no secrets.");
        if (request.Execution.Status != AppModuleExecutionStatuses.Succeeded) return;
        if (!request.Result.Available || string.IsNullOrWhiteSpace(request.Result.Content))
            throw new ArgumentException("A successful validation requires its structured result.");
        using var document = JsonDocument.Parse(request.Result.Content);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("health", out var health)
            || health.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The validation result requires a health object.");
        InfrastructureHostValidationSnapshot.Validate(document.RootElement);
    }

    private static void ApplyExecution(InfrastructureWorkflowExecution operation,
        InfrastructureCommandResultRequest request)
    {
        var succeeded = request.Execution.Status == AppModuleExecutionStatuses.Succeeded;
        operation.Status = succeeded ? InfrastructureWorkflowExecutionStatuses.Succeeded
            : InfrastructureWorkflowExecutionStatuses.Failed;
        operation.StartedAtUtc = request.Execution.StartedAtUtc;
        operation.CompletedAtUtc = DateTime.UtcNow;
        operation.ExitCode = request.Execution.ExitCode;
        operation.Error = succeeded ? null : Limit(request.Execution.FailureSummary ?? request.Execution.ErrorCode);
        operation.ResultJson = succeeded ? request.Result.Content!.Trim() : null;
        operation.Logs.Add(new InfrastructureWorkflowExecutionLog
        {
            Id = Guid.NewGuid(), ExecutionId = operation.Id, Sequence = 0,
            Stream = succeeded ? "stdout" : "stderr",
            Message = Limit(operation.ResultJson ?? operation.Error, 10000)
        });
    }

    private async Task ApplyHostAsync(RunnerHost host, InfrastructureWorkflowExecution operation,
        CancellationToken cancellationToken)
    {
        if (operation.Status == InfrastructureWorkflowExecutionStatuses.Succeeded)
            InfrastructureHostValidationSnapshot.Apply(host, operation.ResultJson!);
        else
        {
            host.ProvisioningStatus = RunnerHostProvisioningStatuses.ValidationFailed;
            host.ProvisioningFailureCode = "HostValidationExecutionFailed";
            host.ProvisioningFailureMessage = operation.Error;
            host.HostedRunnerSchedulingEnabled = false;
            host.ValidatedAtUtc = null;
        }
        var provisioning = await db.RunnerHostProvisioningOperations.Where(item =>
            item.RunnerHostId == host.Id && (item.Status == RunnerHostProvisioningOperationStatuses.Running
                || item.Status == RunnerHostProvisioningOperationStatuses.AwaitingInfrastructureRunner))
            .OrderByDescending(item => item.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (provisioning is null) return;
        provisioning.Status = host.ProvisioningStatus is RunnerHostProvisioningStatuses.Ready
            or RunnerHostProvisioningStatuses.AwaitingApproval
            ? RunnerHostProvisioningOperationStatuses.Succeeded : RunnerHostProvisioningOperationStatuses.Failed;
        provisioning.FailureCode = host.ProvisioningFailureCode;
        provisioning.Error = host.ProvisioningFailureMessage;
        provisioning.FinishedAtUtc = DateTime.UtcNow;
    }

    private static string Limit(string? value, int maximum = 2000)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value[..Math.Min(value.Length, maximum)];
}
