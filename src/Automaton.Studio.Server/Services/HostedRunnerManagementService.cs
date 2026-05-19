using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

public class HostedRunnerManagementService
{
    private readonly ApplicationDbContext db;
    private readonly HostedRunnerProvisioningService provisioningService;
    private readonly IHostedRunnerRemoteCommandExecutor remoteCommandExecutor;
    private readonly HostedRunnerOptions options;
    private readonly UserContextService userContextService;
    private readonly ILogger<HostedRunnerManagementService> logger;

    private Guid UserId => userContextService.GetUserId();

    public HostedRunnerManagementService(
        ApplicationDbContext db,
        HostedRunnerProvisioningService provisioningService,
        IHostedRunnerRemoteCommandExecutor remoteCommandExecutor,
        IOptionsSnapshot<HostedRunnerOptions> options,
        UserContextService userContextService,
        ILogger<HostedRunnerManagementService> logger)
    {
        this.db = db;
        this.provisioningService = provisioningService;
        this.remoteCommandExecutor = remoteCommandExecutor;
        this.options = options.Value;
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

    public async Task RestartOwnedHostedRunnerByRunnerIdAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
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

        var deployment = AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentActions.Restart,
            HostedRunnerDeploymentStatuses.Running,
            $"docker restart {hostedRunner.ContainerName}",
            "Restart requested.");

        await db.SaveChangesAsync(cancellationToken);

        var script = string.Join('\n', new[]
        {
            "set -eu",
            $"docker restart {HostedRunnerDockerCommandBuilder.ShellQuote(hostedRunner.ContainerName)}"
        });

        var executionResult = await remoteCommandExecutor.ExecuteScriptAsync(host, script, cancellationToken);
        if (!executionResult.Succeeded)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = BuildRemoteFailureMessage(executionResult);
            hostedRunner.UpdatedAt = DateTime.UtcNow;
            CompleteDeployment(
                deployment,
                HostedRunnerDeploymentStatuses.Failed,
                executionResult.StandardOutput,
                hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
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

        logger.LogInformation("Hosted runner {HostedRunnerId} restarted on host {HostId}", hostedRunner.Id, host.Id);
        return hostedRunner;
    }

    private async Task<HostedRunner> ReprovisionCoreAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        if (hostedRunner.SubscriptionPlanId == Guid.Empty)
        {
            throw new InvalidOperationException("Hosted runner has no subscription plan.");
        }

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

        var reprovisioned = await provisioningService.EnsureHostedRunnerForUserPlanAsync(
            hostedRunner.UserId,
            hostedRunner.SubscriptionPlanId,
            cancellationToken);

        if (reprovisioned == null)
        {
            throw new InvalidOperationException("Hosted runner reprovisioning could not start.");
        }

        return reprovisioned;
    }

    private async Task<HostedRunner> LoadHostedRunnerAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .Include(x => x.RunnerHost)
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
