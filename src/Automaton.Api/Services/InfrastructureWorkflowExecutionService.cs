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

            if (result.Succeeded && execution.ResultJson != null)
            {
                ApplyHostValidationSnapshot(host, execution.ResultJson);
            }

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
        hostname_value=$(hostname 2>/dev/null || printf 'unknown')
        distribution=$(awk -F= '$1=="ID"{gsub(/"/,"",$2); print $2}' /etc/os-release 2>/dev/null || printf 'unknown')
        os_version=$(awk -F= '$1=="VERSION_ID"{gsub(/"/,"",$2); print $2}' /etc/os-release 2>/dev/null || printf 'unknown')
        kernel=$(uname -r 2>/dev/null || printf 'unknown')
        architecture=$(uname -m 2>/dev/null || printf 'unknown')
        cpu_count=$(nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null || printf '0')
        cpu_model=$(awk -F: '/model name|Hardware/{gsub(/^ +/,"",$2); print $2; exit}' /proc/cpuinfo 2>/dev/null || printf 'unknown')
        memory=$(free -m 2>/dev/null | awk '/^Mem:/{print $2}' || printf '0')
        disk=$(df -Pm / 2>/dev/null | awk 'NR==2{print $2}' || printf '0')
        docker_version=$(docker version --format '{{.Server.Version}}' 2>/dev/null || printf 'unavailable')
        docker_status=$(docker info >/dev/null 2>&1 && printf 'running' || printf 'unavailable')
        docker_storage_driver=$(docker info --format '{{.Driver}}' 2>/dev/null || printf 'unknown')
        docker_cgroup_version=$(docker info --format '{{.CgroupVersion}}' 2>/dev/null || printf 'unknown')
        network_status=$(docker network inspect '__DOCKER_NETWORK__' >/dev/null 2>&1 && printf 'present' || printf 'missing')
        runner_container=$(docker ps --filter 'name=automaton-infrastructure-runner' --format '{{.Status}}' 2>/dev/null || printf 'unknown')
        ports=$(ss -lnt 2>/dev/null | awk 'NR>1{print $4}' | head -50 | paste -sd ',' - || printf 'unknown')
        interfaces=$(ip -o link show 2>/dev/null | awk -F': ' '{print $2}' | paste -sd ',' - || printf 'unknown')
        private_ip=$(ip route get 1.1.1.1 2>/dev/null | awk '{for (i=1;i<=NF;i++) if ($i=="src") {print $(i+1); exit}}' || printf 'unknown')
        timestamp_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
        docker_health=$(test "$docker_status" = running && printf 'Healthy' || printf 'Unhealthy')
        network_health=$(test "$network_status" = present && printf 'Healthy' || printf 'Unhealthy')
        printf '{"totalCpuCores":%s,"totalMemoryMb":%s,"totalDiskMb":%s,"metadata":{"hostname":"%s","os":{"distribution":"%s","version":"%s","kernel":"%s"},"architecture":"%s","docker":{"version":"%s","status":"%s","storageDriver":"%s","cgroupVersion":"%s"},"cpu":{"model":"%s","cores":%s},"memoryMb":%s,"diskMb":%s,"network":{"privateIp":"%s","interfaces":"%s","automatonNetwork":"%s","availableRuntimePorts":"%s"},"runtime":{"infrastructureRunnerContainer":"%s"}},"health":{"validationVersion":"host-validation.v1","timestampUtc":"%s","docker":"%s","network":"%s","disk":"%s","apiConnectivity":"Healthy","warnings":[],"errors":[]}}\n' \
          "${cpu_count:-0}" "${memory:-0}" "${disk:-0}" "$(json_escape "$hostname_value")" "$(json_escape "$distribution")" "$(json_escape "$os_version")" "$(json_escape "$kernel")" "$(json_escape "$architecture")" "$(json_escape "$docker_version")" "$(json_escape "$docker_status")" "$(json_escape "$docker_storage_driver")" "$(json_escape "$docker_cgroup_version")" "$(json_escape "$cpu_model")" "${cpu_count:-0}" "${memory:-0}" "${disk:-0}" "$(json_escape "$private_ip")" "$(json_escape "$interfaces")" "$(json_escape "$network_status")" "$(json_escape "$ports")" "$(json_escape "$runner_container")" "$(json_escape "$timestamp_utc")" "$(json_escape "$docker_health")" "$(json_escape "$network_health")" "$(test "${disk:-0}" -gt 0 2>/dev/null && printf 'Healthy' || printf 'Unhealthy')"
        """.Replace("__DOCKER_NETWORK__", safeNetwork, StringComparison.Ordinal);
    }

    private static void ApplyHostValidationSnapshot(RunnerHost host, string resultJson)
    {
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        if (root.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object)
        {
            host.MetadataJson = metadata.GetRawText();
        }

        if (root.TryGetProperty("health", out var health)
            && health.ValueKind == JsonValueKind.Object)
        {
            host.HealthJson = health.GetRawText();
        }

        if (root.TryGetProperty("metadata", out var metadataRoot)
            && metadataRoot.TryGetProperty("network", out var network)
            && network.TryGetProperty("privateIp", out var privateIp)
            && privateIp.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(privateIp.GetString())
            && !string.Equals(privateIp.GetString(), "unknown", StringComparison.OrdinalIgnoreCase))
        {
            host.PrivateIp = privateIp.GetString();
        }

        if (TryGetDecimal(root, "totalCpuCores", out var totalCpuCores) && totalCpuCores > 0)
        {
            host.TotalCpuCores = totalCpuCores;
        }

        if (TryGetInt32(root, "totalMemoryMb", out var totalMemoryMb) && totalMemoryMb > 0)
        {
            host.TotalMemoryMb = totalMemoryMb;
        }

        if (TryGetInt32(root, "totalDiskMb", out var totalDiskMb) && totalDiskMb > 0)
        {
            host.TotalDiskMb = totalDiskMb;
        }

        host.UpdatedAt = DateTime.UtcNow;
    }

    private static bool TryGetDecimal(JsonElement root, string propertyName, out decimal value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetDecimal(out value);
    }

    private static bool TryGetInt32(JsonElement root, string propertyName, out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
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
