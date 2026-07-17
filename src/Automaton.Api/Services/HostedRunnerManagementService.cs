using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class HostedRunnerManagementService
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerProvisioningService provisioningService;
    private readonly IHostedRunnerRemoteCommandExecutorResolver remoteCommandExecutorResolver;
    private readonly HostedRunnerOptions options;
    private readonly IAuditEventService auditEventService;
    private readonly UserContextService userContextService;
    private readonly ILogger<HostedRunnerManagementService> logger;

    private Guid UserId => userContextService.GetUserId();

    public HostedRunnerManagementService(
        ApplicationDbContext db,
        HostedRunnerProvisioningService provisioningService,
        IHostedRunnerRemoteCommandExecutorResolver remoteCommandExecutorResolver,
        IOptionsSnapshot<HostedRunnerOptions> options,
        IAuditEventService auditEventService,
        UserContextService userContextService,
        ILogger<HostedRunnerManagementService> logger)
    {
        this.db = db;
        this.provisioningService = provisioningService;
        this.remoteCommandExecutorResolver = remoteCommandExecutorResolver;
        this.options = options.Value;
        this.auditEventService = auditEventService;
        this.userContextService = userContextService;
        this.logger = logger;
    }

    public async Task<HostedRunner> RestartAsAdminAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        return await RestartCoreAsync(hostedRunner, cancellationToken);
    }

    public async Task<HostedRunner> ReprovisionAsAdminAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        return await ReprovisionCoreAsync(hostedRunner, cancellationToken);
    }

    public async Task DeleteAsAdminAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadHostedRunnerAsync(hostedRunnerId, cancellationToken);
        var host = hostedRunner.RunnerHost;

        hostedRunner.Status = HostedRunnerStatuses.Deleting;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        var appAssignments = await db.WebApps
            .Where(x => x.ExecutionRunnerId == hostedRunner.RunnerId)
            .ToListAsync(cancellationToken);
        foreach (var app in appAssignments)
        {
            app.ExecutionRunnerId = null;
        }

        AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Deprovision,
            HostedRunnerDeploymentStatuses.Running,
            $"docker rm -f {HostedRunnerDockerCommandBuilder.ShellQuote(hostedRunner.ContainerName)}",
            "Hosted runner deletion requested.");
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (host != null && !string.IsNullOrWhiteSpace(hostedRunner.ContainerName))
            {
                var executor = remoteCommandExecutorResolver.Resolve(host, options);
                var script = string.Join('\n', new[]
                {
                    "set -eu",
                    $"docker rm -f {HostedRunnerDockerCommandBuilder.ShellQuote(hostedRunner.ContainerName)} >/dev/null 2>&1 || true"
                });
                var result = await executor.ExecuteScriptAsync(host, script, cancellationToken);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(BuildRemoteFailureMessage(result));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hostedRunner.Status = HostedRunnerStatuses.DeleteFailed;
            hostedRunner.LastError = ex.Message;
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            var deployment = hostedRunner.Deployments.OrderByDescending(x => x.StartedAt).First();
            CompleteDeployment(deployment, HostedRunnerDeploymentStatuses.Failed, null, ex.Message);
            await db.SaveChangesAsync(cancellationToken);
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Failed,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.HostedRunnerDeleteFailed,
                Outcome = AuditOutcomes.Failed,
                Message = $"Hosted runner '{hostedRunner.Name}' deletion failed",
                TargetUserId = hostedRunner.UserId,
                Metadata = new { hostedRunner.Name, hostedRunner.ContainerName, Error = ex.Message }
            }, cancellationToken);
            throw;
        }

        var completedDeployment = hostedRunner.Deployments.OrderByDescending(x => x.StartedAt).First();
        CompleteDeployment(completedDeployment, HostedRunnerDeploymentStatuses.Succeeded, null, null);
        hostedRunner.ApiKeyId = null;
        hostedRunner.RunnerId = null;
        hostedRunner.ContainerId = null;
        hostedRunner.RuntimeEndpoint = null;
        hostedRunner.Status = HostedRunnerStatuses.Deleted;
        hostedRunner.DeletedAt = DateTime.UtcNow;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = hostedRunner.Id,
            EventType = AuditEventTypes.HostedRunnerDeleted,
            Outcome = AuditOutcomes.Success,
            Message = $"Hosted runner '{hostedRunner.Name}' was deleted",
            TargetUserId = hostedRunner.UserId,
            Metadata = new
            {
                hostedRunner.Name,
                hostedRunner.RunnerId,
                hostedRunner.RunnerHostId,
                hostedRunner.ContainerName
            }
        }, cancellationToken);

    }

    public async Task RestartOwnedHostedRunnerByRunnerIdAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
                .ThenInclude(x => x!.InfrastructureRunner)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x =>
                x.RunnerId == runnerId &&
                x.UserId == UserId &&
                x.DeletedAt == null,
                cancellationToken);

        if (hostedRunner == null)
        {
            throw new InvalidOperationException("Hosted runner not found for this runner.");
        }

        await RestartCoreAsync(hostedRunner, cancellationToken);
    }

    private async Task<HostedRunner> RestartCoreAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        var host = hostedRunner.RunnerHost;
        if (host == null)
        {
            throw new InvalidOperationException("Hosted runner has no assigned host.");
        }

        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Requested,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.RunnerRestartRequested,
                Message = $"Hosted runner '{hostedRunner.Name}' restart was requested",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    RunnerHostName = host.Name,
                    hostedRunner.ContainerName
                }
            },
            cancellationToken);

        var deployment = AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Restart,
            HostedRunnerDeploymentStatuses.Running,
            $"docker restart {hostedRunner.ContainerName}",
            "Restart requested.");

        await db.SaveChangesAsync(cancellationToken);

        IHostedRunnerRemoteCommandExecutor executor;
        try
        {
            executor = remoteCommandExecutorResolver.Resolve(host, options);
        }
        catch (InvalidOperationException ex)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = ex.Message;
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            CompleteDeployment(
                deployment,
                HostedRunnerDeploymentStatuses.Failed,
                null,
                hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(hostedRunner.LastError);
        }
        var script = string.Join('\n', new[]
        {
            "set -eu",
            $"docker restart {HostedRunnerDockerCommandBuilder.ShellQuote(hostedRunner.ContainerName)}"
        });

        var executionResult = await executor.ExecuteScriptAsync(host, script, cancellationToken);
        if (!executionResult.Succeeded)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = BuildRemoteFailureMessage(executionResult);
            var failureSummary = $"Hosted runner restart failed with exit status {executionResult.ExitStatus}.";
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            CompleteDeployment(
                deployment,
                HostedRunnerDeploymentStatuses.Failed,
                executionResult.StandardOutput,
                hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            await auditEventService.WriteAsync(
                new AuditEventWriteRequest
                {
                    Action = AuditActions.Failed,
                    EntityType = AuditEntityTypes.HostedRunner,
                    EntityId = hostedRunner.Id,
                    EventType = AuditEventTypes.RunnerRestartFailed,
                    Outcome = AuditOutcomes.Failed,
                    Message = $"Hosted runner '{hostedRunner.Name}' restart failed",
                    TargetUserId = hostedRunner.UserId,
                    Metadata = new
                    {
                        hostedRunner.Name,
                        hostedRunner.RunnerId,
                        hostedRunner.RunnerHostId,
                        RunnerHostName = host.Name,
                        hostedRunner.ContainerName,
                        FailureSummary = failureSummary
                    }
                },
                cancellationToken);
            throw new InvalidOperationException(hostedRunner.LastError);
        }

        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        CompleteDeployment(
            deployment,
            HostedRunnerDeploymentStatuses.Succeeded,
            executionResult.StandardOutput,
            string.IsNullOrWhiteSpace(executionResult.StandardError) ? null : executionResult.StandardError);
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Succeeded,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.RunnerRestartSucceeded,
                Outcome = AuditOutcomes.Success,
                Message = $"Hosted runner '{hostedRunner.Name}' restart succeeded",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    RunnerHostName = host.Name,
                    hostedRunner.ContainerName
                }
            },
            cancellationToken);

        logger.LogInformation("Hosted runner {HostedRunnerId} restarted on host {HostId}", hostedRunner.Id, host.Id);
        return hostedRunner;
    }

    private async Task<HostedRunner> ReprovisionCoreAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Requested,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.RunnerReprovisionRequested,
                Message = $"Hosted runner '{hostedRunner.Name}' reprovision was requested",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    hostedRunner.HostedRunnerSpecificationId,
                    hostedRunner.ContainerName
                }
            },
            cancellationToken);

        if (hostedRunner.Runner != null)
        {
            hostedRunner.Runner.ConnectionId = null;
        }

        hostedRunner.RunnerId = null;
        hostedRunner.Status = options.Enabled ? HostedRunnerStatuses.Provisioning : HostedRunnerStatuses.Pending;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;

        AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Reprovision,
            HostedRunnerDeploymentStatuses.Succeeded,
            null,
            "Hosted runner reprovision requested. Starting a fresh deployment cycle.");

        await db.SaveChangesAsync(cancellationToken);

        var reprovisioned = await provisioningService.EnsureHostedRunnerAsync(
            hostedRunner.UserId,
            cancellationToken);

        if (reprovisioned == null)
        {
            await auditEventService.WriteAsync(
                new AuditEventWriteRequest
                {
                    Action = AuditActions.Failed,
                    EntityType = AuditEntityTypes.HostedRunner,
                    EntityId = hostedRunner.Id,
                    EventType = AuditEventTypes.RunnerReprovisionFailed,
                    Outcome = AuditOutcomes.Failed,
                    Message = $"Hosted runner '{hostedRunner.Name}' reprovision could not start",
                    TargetUserId = hostedRunner.UserId,
                    Metadata = new
                    {
                        hostedRunner.Name,
                        hostedRunner.RunnerId,
                        hostedRunner.RunnerHostId,
                        hostedRunner.HostedRunnerSpecificationId
                    }
                },
                cancellationToken);
            throw new InvalidOperationException("Hosted runner reprovisioning could not start.");
        }

        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Succeeded,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = reprovisioned.Id,
                EventType = AuditEventTypes.RunnerReprovisionSucceeded,
                Outcome = AuditOutcomes.Success,
                Message = $"Hosted runner '{reprovisioned.Name}' reprovision started",
                TargetUserId = reprovisioned.UserId,
                Metadata = new
                {
                    reprovisioned.Name,
                    reprovisioned.RunnerId,
                    reprovisioned.RunnerHostId,
                    reprovisioned.HostedRunnerSpecificationId,
                    reprovisioned.Status
                }
            },
            cancellationToken);

        return reprovisioned;
    }

    private async Task<HostedRunner> LoadHostedRunnerAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
                .ThenInclude(x => x!.InfrastructureRunner)
            .Include(x => x.Runner)
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.Id == hostedRunnerId && x.DeletedAt == null, cancellationToken);

        if (hostedRunner == null)
        {
            throw new InvalidOperationException("Hosted runner not found.");
        }

        return hostedRunner;
    }

    private HostedRunnerDeployment AddDeployment(
        HostedRunner runner,
        string action,
        string status,
        string? commandPreview,
        string? message)
    {
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = runner.Id,
            Action = action,
            Status = status,
            CommandPreview = commandPreview,
            Logs = status is HostedRunnerDeploymentStatuses.Failed ? null : message,
            Error = status is HostedRunnerDeploymentStatuses.Failed ? message : null,
            StartedAt = DateTime.UtcNow,
            FinishedAt = status is HostedRunnerDeploymentStatuses.Running ? null : DateTime.UtcNow
        };

        db.HostedRunnerDeployments.Add(deployment);
        return deployment;
    }

    private static void CompleteDeployment(
        HostedRunnerDeployment deployment,
        string status,
        string? logs,
        string? error)
    {
        deployment.Status = status;
        deployment.Logs = logs;
        deployment.Error = error;
        deployment.FinishedAt = DateTime.UtcNow;
    }

    private static string BuildRemoteFailureMessage(HostedRunnerRemoteExecutionResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError;
        }

        return $"Remote command failed with exit status {result.ExitStatus}.";
    }
}
