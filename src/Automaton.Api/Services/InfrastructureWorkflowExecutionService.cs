using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Api.Config;
using Automaton.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureWorkflowExecutionService(
    ApplicationDbContext db,
    IInfrastructureRunnerCommandExecutor commandExecutor,
    IAuditEventService auditEventService,
    IOptions<HostedRunnerOptions> hostedRunnerOptions,
    ILogger<InfrastructureWorkflowExecutionService> logger)
{
    private const string HostValidationWorkflowVersion = "host-validation.v1";

    public async Task<InfrastructureWorkflowExecutionResponse> StartHostValidationAsync(
        StartHostValidationRequest request,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        if (request.RunnerHostId == Guid.Empty)
        {
            throw new ArgumentException("Runner host is required.");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"host-validation:{request.RunnerHostId:N}:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();

        var existing = await db.InfrastructureWorkflowExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing != null)
        {
            return Map(existing);
        }

        var host = await db.RunnerHosts
            .Include(x => x.InfrastructureRunner)
            .FirstOrDefaultAsync(x => x.Id == request.RunnerHostId, cancellationToken)
            ?? throw new InvalidOperationException("The target Runner host was not found.");

        var runner = host.InfrastructureRunner;
        if (runner == null || !host.InfrastructureRunnerId.HasValue)
        {
            throw new InvalidOperationException("The Runner host has no linked Infrastructure Runner.");
        }

        if (request.RunnerId.HasValue && request.RunnerId != runner.Id)
        {
            throw new InvalidOperationException("The selected Runner is not the active Infrastructure Runner for this host.");
        }

        if (!RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.WorkflowExecution, runner.Purpose)
            || !RunnerCapabilities.Includes(runner.CapabilitiesJson, RunnerCapabilities.HostExecution, runner.Purpose))
        {
            throw new InvalidOperationException("The Infrastructure Runner does not have the capabilities required for host validation.");
        }

        var execution = new InfrastructureWorkflowExecution
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

        db.InfrastructureWorkflowExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);

        execution.Status = InfrastructureWorkflowExecutionStatuses.Running;
        execution.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await commandExecutor.ExecuteScriptAsync(
                host,
                BuildHostValidationScript(hostedRunnerOptions.Value.DockerNetwork),
                cancellationToken);
            execution.ExitCode = result.ExitStatus;
            execution.ResultJson = result.Succeeded && LooksLikeJson(result.StandardOutput)
                ? result.StandardOutput.Trim()
                : null;
            execution.Error = result.Succeeded ? null : Limit(result.StandardError);
            execution.Status = result.Succeeded
                ? InfrastructureWorkflowExecutionStatuses.Succeeded
                : InfrastructureWorkflowExecutionStatuses.Failed;

            AppendLogs(execution, result.StandardOutput, "stdout");
            AppendLogs(execution, result.StandardError, "stderr");
        }
        catch (OperationCanceledException)
        {
            execution.Status = InfrastructureWorkflowExecutionStatuses.Cancelled;
            execution.Error = "Workflow execution was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            execution.Status = InfrastructureWorkflowExecutionStatuses.Failed;
            execution.Error = Limit(ex.Message);
            AppendLog(execution, "stderr", ex.Message);
            logger.LogError(ex, "Infrastructure workflow execution {ExecutionId} failed", execution.Id);
        }

        execution.CompletedAtUtc = DateTime.UtcNow;
        db.InfrastructureWorkflowExecutions.Update(execution);
        db.InfrastructureWorkflowExecutionLogs.AddRange(execution.Logs);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.Runner,
            EntityId = execution.TargetRunnerId,
            EventType = AuditEventTypes.InfrastructureWorkflowExecuted,
            Message = $"Infrastructure workflow '{execution.WorkflowVersionId}' completed with status '{execution.Status}'.",
            Outcome = execution.Status == InfrastructureWorkflowExecutionStatuses.Succeeded
                ? AuditOutcomes.Success
                : AuditOutcomes.Failed,
            ActorUserId = requestedByUserId,
            Metadata = new { execution.Id, execution.TargetRunnerHostId, execution.TargetRunnerId, execution.Status }
        }, cancellationToken);

        return Map(execution);
    }

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

    private static string BuildHostValidationScript(string dockerNetwork)
    {
        var safeNetwork = dockerNetwork.Replace("'", "'\\\"'\\\"'", StringComparison.Ordinal);
        return """
        set -u
        json_escape() {
          printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e ':a' -e 'N' -e '$!ba' -e 's/\n/\\n/g'
        }
        os_name=$(uname -srm 2>/dev/null || printf 'unknown')
        kernel=$(uname -r 2>/dev/null || printf 'unknown')
        cpu_count=$(nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null || printf 'unknown')
        memory=$(free -m 2>/dev/null | awk '/^Mem:/{print $2}' || printf 'unknown')
        disk=$(df -Pm / 2>/dev/null | awk 'NR==2{print $2}' || printf 'unknown')
        docker_version=$(docker --version 2>/dev/null || printf 'unavailable')
        docker_status=$(docker info >/dev/null 2>&1 && printf 'running' || printf 'unavailable')
        network_status=$(docker network inspect '__DOCKER_NETWORK__' >/dev/null 2>&1 && printf 'present' || printf 'missing')
        runner_container=$(docker ps --filter 'name=automaton-infrastructure-runner' --format '{{.Status}}' 2>/dev/null || printf 'unknown')
        ports=$(ss -lnt 2>/dev/null | awk 'NR>1{print $4}' | head -50 | paste -sd ',' - || printf 'unknown')
        printf '{"operatingSystem":"%s","kernel":"%s","cpuCount":"%s","memoryMb":"%s","rootDiskMb":"%s","dockerVersion":"%s","dockerStatus":"%s","automatonNetwork":"%s","infrastructureRunnerContainer":"%s","availableRuntimePorts":"%s","hostToApiConnectivity":"verified_by_runner_connection"}\n' \
          "$(json_escape "$os_name")" "$(json_escape "$kernel")" "$(json_escape "$cpu_count")" "$(json_escape "$memory")" "$(json_escape "$disk")" "$(json_escape "$docker_version")" "$(json_escape "$docker_status")" "$(json_escape "$network_status")" "$(json_escape "$runner_container")" "$(json_escape "$ports")"
        """.Replace("__DOCKER_NETWORK__", safeNetwork, StringComparison.Ordinal);
    }

    private static void AppendLogs(InfrastructureWorkflowExecution execution, string text, string stream)
    {
        foreach (var line in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            AppendLog(execution, stream, line);
        }
    }

    private static void AppendLog(InfrastructureWorkflowExecution execution, string stream, string message)
        => execution.Logs.Add(new InfrastructureWorkflowExecutionLog
        {
            Id = Guid.NewGuid(),
            ExecutionId = execution.Id,
            Sequence = execution.Logs.Count,
            Stream = stream,
            Message = Limit(message, 10000)
        });

    private static bool LooksLikeJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Limit(string? value, int max = 2000)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];

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
