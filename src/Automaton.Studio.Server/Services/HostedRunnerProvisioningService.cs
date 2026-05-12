using Automaton.Core.Enums;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

public class HostedRunnerProvisioningService
{
    private static readonly string[] OccupyingStatuses =
    [
        HostedRunnerStatuses.Pending,
        HostedRunnerStatuses.Provisioning,
        HostedRunnerStatuses.Running
    ];

    private readonly ApplicationDbContext db;
    private readonly ApiKeyService apiKeyService;
    private readonly IHostedRunnerRemoteCommandExecutor remoteCommandExecutor;
    private readonly HostedRunnerOptions options;
    private readonly ILogger<HostedRunnerProvisioningService> logger;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        ApiKeyService apiKeyService,
        IHostedRunnerRemoteCommandExecutor remoteCommandExecutor,
        IOptionsSnapshot<HostedRunnerOptions> options,
        ILogger<HostedRunnerProvisioningService> logger)
    {
        this.db = db;
        this.apiKeyService = apiKeyService;
        this.remoteCommandExecutor = remoteCommandExecutor;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<HostedRunner?> EnsureHostedRunnerForUserPlanAsync(Guid userId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken);
        if (plan == null)
        {
            return null;
        }

        var capabilities = PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson).HostedRunner;
        if (!capabilities.Enabled)
        {
            await DisableHostedRunnersForUserAsync(userId, "Assigned plan does not include a hosted runner.", cancellationToken);
            return null;
        }

        var hostedRunner = await db.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.DeletedAt == null && x.Status != HostedRunnerStatuses.Disabled, cancellationToken);

        var now = DateTime.UtcNow;
        if (hostedRunner == null)
        {
            hostedRunner = new HostedRunner
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now,
                Name = BuildRunnerName(userId),
                ContainerName = BuildContainerName(userId)
            };
            db.HostedRunners.Add(hostedRunner);
        }

        hostedRunner.SubscriptionPlanId = plan.Id;
        hostedRunner.CpuCores = capabilities.CpuCores;
        hostedRunner.MemoryMb = capabilities.MemoryMb;
        hostedRunner.DiskMb = capabilities.DiskMb;
        hostedRunner.MaxApps = capabilities.MaxApps;
        hostedRunner.UpdatedAt = now;

        var host = await SelectHostAsync(capabilities, cancellationToken);
        if (host == null)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = "No active runner host has enough available capacity.";
            AddDeployment(hostedRunner, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            return hostedRunner;
        }

        hostedRunner.RunnerHostId = host.Id;
        hostedRunner.LastError = null;

        if (hostedRunner.RunnerId != null && hostedRunner.Status == HostedRunnerStatuses.Running)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Hosted runner {HostedRunnerId} is already registered; plan metadata was refreshed without redeploying.", hostedRunner.Id);
            return hostedRunner;
        }

        var rawApiKey = await EnsureProvisioningApiKeyAsync(hostedRunner, userId, cancellationToken);

        hostedRunner.Status = options.Enabled
            ? HostedRunnerStatuses.Provisioning
            : HostedRunnerStatuses.Pending;

        var commandPreview = HostedRunnerDockerCommandBuilder.BuildPreview(hostedRunner, options);
        var deployment = AddDeployment(
            hostedRunner,
            options.Enabled ? HostedRunnerDeploymentStatuses.Running : HostedRunnerDeploymentStatuses.Skipped,
            commandPreview,
            options.Enabled
                ? $"Provisioning mode '{options.DeploymentMode}' selected."
                : "Hosted runner provisioning is disabled. Enable HostedRunners:Enabled after adding a host and validating capacity.");

        await db.SaveChangesAsync(cancellationToken);

        if (!options.Enabled)
        {
            logger.LogInformation("Hosted runner {HostedRunnerId} prepared in dry-run mode for user {UserId}", hostedRunner.Id, userId);
            return hostedRunner;
        }

        if (options.DeploymentMode != HostedRunnerDeploymentModes.SshDocker)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = $"Unsupported hosted runner deployment mode '{options.DeploymentMode}'.";
            CompleteDeployment(deployment, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            return hostedRunner;
        }

        var provisionScript = HostedRunnerDockerCommandBuilder.BuildProvisionScript(hostedRunner, options, rawApiKey);
        var executionResult = await remoteCommandExecutor.ExecuteProvisionScriptAsync(host, provisionScript, cancellationToken);
        if (!executionResult.Succeeded)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = BuildRemoteFailureMessage(executionResult);
            CompleteDeployment(deployment, HostedRunnerDeploymentStatuses.Failed, executionResult.StandardOutput, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            return hostedRunner;
        }

        hostedRunner.ContainerId = executionResult.ContainerId;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        CompleteDeployment(
            deployment,
            HostedRunnerDeploymentStatuses.Succeeded,
            executionResult.StandardOutput,
            string.IsNullOrWhiteSpace(executionResult.StandardError) ? null : executionResult.StandardError);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Hosted runner {HostedRunnerId} prepared for user {UserId} on host {HostId}", hostedRunner.Id, userId, host.Id);
        return hostedRunner;
    }

    public async Task DisableHostedRunnersForUserAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var runners = await db.HostedRunners
            .Where(x => x.UserId == userId && x.DeletedAt == null && x.Status != HostedRunnerStatuses.Disabled)
            .ToListAsync(cancellationToken);

        foreach (var runner in runners)
        {
            runner.Status = HostedRunnerStatuses.Disabled;
            runner.LastError = reason;
            runner.UpdatedAt = DateTime.UtcNow;
            AddDeployment(runner, HostedRunnerDeploymentStatuses.Skipped, null, reason, HostedRunnerDeploymentActions.Deprovision);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RunnerHost?> SelectHostAsync(HostedRunnerCapability requested, CancellationToken cancellationToken)
    {
        var hosts = await db.RunnerHosts
            .Include(x => x.HostedRunners)
            .Where(x => x.Status == RunnerHostStatuses.Active)
            .OrderByDescending(x => x.IsLocal)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return hosts.FirstOrDefault(host =>
        {
            var assigned = host.HostedRunners.Where(r => OccupyingStatuses.Contains(r.Status) && r.DeletedAt == null);
            return assigned.Sum(r => r.CpuCores) + requested.CpuCores <= host.TotalCpuCores
                && assigned.Sum(r => r.MemoryMb) + requested.MemoryMb <= host.TotalMemoryMb
                && assigned.Sum(r => r.DiskMb) + requested.DiskMb <= host.TotalDiskMb;
        });
    }

    private async Task<string> EnsureProvisioningApiKeyAsync(HostedRunner hostedRunner, Guid userId, CancellationToken cancellationToken)
    {
        var requiresFreshRawKey = options.Enabled && options.DeploymentMode == HostedRunnerDeploymentModes.SshDocker;
        if (!requiresFreshRawKey && hostedRunner.ApiKeyId != null)
        {
            return string.Empty;
        }

        if (requiresFreshRawKey && hostedRunner.ApiKeyId != null && hostedRunner.RunnerId == null)
        {
            var priorKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == hostedRunner.ApiKeyId, cancellationToken);
            if (priorKey != null)
            {
                db.ApiKeys.Remove(priorKey);
                hostedRunner.ApiKeyId = null;
            }
        }

        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            userId,
            $"{hostedRunner.Name} key",
            "Generated for hosted runner container provisioning.",
            null,
            ApiKeyScope.Runner);

        hostedRunner.ApiKeyId = apiKey.Id;
        return rawApiKey;
    }

    private static HostedRunnerDeployment AddDeployment(
        HostedRunner runner,
        string status,
        string? commandPreview,
        string? message,
        string action = HostedRunnerDeploymentActions.Provision)
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
        runner.Deployments.Add(deployment);
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

        return $"Remote provisioning command failed with exit status {result.ExitStatus}.";
    }

    private static string BuildRunnerName(Guid userId) => $"Hosted Runner {userId.ToString("N")[..8]}";

    private static string BuildContainerName(Guid userId) => $"automaton-hosted-runner-{userId.ToString("N")[..12]}";
}
