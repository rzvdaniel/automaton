using System.Text.Json;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Core.Models;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Api.Config;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureWorkflowExecutionService(
    ApplicationDbContext db,
    IEventPublisher events,
    ITransactionalEventExecutor transactions,
    IOptions<HostedRunnerOptions> hostedRunnerOptions)
{
    private const string HostValidationWorkflowVersion = HostValidationCommand.WorkflowVersion;

    public async Task<InfrastructureWorkflowExecutionResponse> StartHostValidationAsync(
        StartHostValidationRequest request,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        ValidateHostId(request.RunnerHostId);
        if (request.IdempotencyKey?.Trim().Length > 160)
            throw new ArgumentException("The idempotency key must not exceed 160 characters.");
        var idempotencyKey = ResolveIdempotencyKey(request);
        var existing = await FindExistingExecutionAsync(idempotencyKey, cancellationToken);
        if (existing != null)
        {
            if (existing.TargetRunnerHostId != request.RunnerHostId
                || request.RunnerId.HasValue && existing.TargetRunnerId != request.RunnerId)
                throw new ArgumentException("The idempotency key belongs to a different validation target.");
            return Map(existing);
        }

        var host = await LoadHostAsync(request.RunnerHostId, cancellationToken);
        var runner = ValidateRunnerForHostValidation(host, host.InfrastructureRunner, request.RunnerId);
        var execution = CreateExecution(requestedByUserId, host, runner, idempotencyKey);
        await transactions.ExecuteAsync(async token =>
        {
            db.InfrastructureWorkflowExecutions.Add(execution);
            await db.SaveChangesAsync(token);
            await PublishAsync(execution, token);
        }, cancellationToken);
        return Map(execution);
    }

    private static void ValidateHostId(Guid runnerHostId)
    {
        if (runnerHostId == Guid.Empty)
            throw new ArgumentException("Runner host is required.");
    }

    private static string ResolveIdempotencyKey(StartHostValidationRequest request)
        => string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"host-validation:{request.RunnerHostId:N}:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();

    private Task<InfrastructureWorkflowExecution?> FindExistingExecutionAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
        => db.InfrastructureWorkflowExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<RunnerHost> LoadHostAsync(Guid runnerHostId, CancellationToken cancellationToken)
        => await db.RunnerHosts
            .Include(x => x.InfrastructureRunner)
            .FirstOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
            ?? throw new InvalidOperationException("The target Runner host was not found.");

    private static Runner ValidateRunnerForHostValidation(
        RunnerHost host,
        Runner? runner,
        Guid? requestedRunnerId)
    {
        if (runner == null || !host.InfrastructureRunnerId.HasValue)
            throw new InvalidOperationException("The Runner host has no linked Infrastructure Runner.");
        if (requestedRunnerId.HasValue && requestedRunnerId != runner.Id)
            throw new InvalidOperationException("The selected Runner is not the active Infrastructure Runner for this host.");
        if (!RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.WorkflowExecution, runner.Purpose)
            || !RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.HostExecution, runner.Purpose)
            || !RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.HostedRunnerProvisioning, runner.Purpose))
            throw new InvalidOperationException("The Infrastructure Runner does not have the capabilities required for host validation.");
        if (runner.Purpose != RunnerPurposes.Infrastructure)
            throw new InvalidOperationException("Host validation requires an Infrastructure Runner.");
        return runner;
    }

    private static InfrastructureWorkflowExecution CreateExecution(
        Guid requestedByUserId,
        RunnerHost host,
        Runner runner,
        string idempotencyKey)
        => new()
        {
            Id = Guid.NewGuid(),
            WorkflowVersionId = HostValidationWorkflowVersion,
            RequestedByUserId = requestedByUserId,
            TargetRunnerId = runner.Id,
            TargetRunnerHostId = host.Id,
            Status = InfrastructureWorkflowExecutionStatuses.Queued,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        };

    private Task PublishAsync(InfrastructureWorkflowExecution execution, CancellationToken cancellationToken)
        => events.PublishAsync(new InfrastructureCommandRequestedEvent(
            execution.Id, execution.Id, execution.TargetRunnerHostId, execution.TargetRunnerId,
            HostValidationCommand.ActionKey,
            JsonSerializer.Serialize(new { dockerNetwork = hostedRunnerOptions.Value.DockerNetwork }),
            [], execution.CreatedAtUtc), cancellationToken);

    public async Task<InfrastructureWorkflowExecutionResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await db.InfrastructureWorkflowExecutions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new InfrastructureWorkflowExecutionResponse
            {
                Id = x.Id,
                WorkflowVersionId = x.WorkflowVersionId,
                RequestedByUserId = x.RequestedByUserId,
                TargetRunnerId = x.TargetRunnerId,
                TargetRunnerHostId = x.TargetRunnerHostId,
                Status = x.Status,
                CreatedAtUtc = x.CreatedAtUtc,
                StartedAtUtc = x.StartedAtUtc,
                CompletedAtUtc = x.CompletedAtUtc,
                ExitCode = x.ExitCode,
                Error = x.Error,
                IdempotencyKey = x.IdempotencyKey,
                ResultJson = x.ResultJson
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<InfrastructureWorkflowExecutionLogResponse>> GetLogsAsync(Guid id, CancellationToken cancellationToken)
        => await db.InfrastructureWorkflowExecutionLogs.AsNoTracking()
            .Where(x => x.ExecutionId == id)
            .OrderBy(x => x.Sequence)
            .Select(x => new InfrastructureWorkflowExecutionLogResponse
            {
                Sequence = x.Sequence,
                TimestampUtc = x.TimestampUtc,
                Stream = x.Stream,
                Message = x.Message
            })
            .ToListAsync(cancellationToken);

    private static InfrastructureWorkflowExecutionResponse Map(InfrastructureWorkflowExecution entity)
        => new()
        {
            Id = entity.Id,
            WorkflowVersionId = entity.WorkflowVersionId,
            RequestedByUserId = entity.RequestedByUserId,
            TargetRunnerId = entity.TargetRunnerId,
            TargetRunnerHostId = entity.TargetRunnerHostId,
            Status = entity.Status,
            CreatedAtUtc = entity.CreatedAtUtc,
            StartedAtUtc = entity.StartedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
            ExitCode = entity.ExitCode,
            Error = entity.Error,
            IdempotencyKey = entity.IdempotencyKey,
            ResultJson = entity.ResultJson
        };
}
