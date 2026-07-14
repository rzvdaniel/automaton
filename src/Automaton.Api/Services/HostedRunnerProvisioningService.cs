using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

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
    private readonly IHostedRunnerRemoteCommandExecutorResolver remoteCommandExecutorResolver;
    private readonly HostedRunnerOptions options;
    private readonly IAuditEventService auditEventService;
    private readonly ILogger<HostedRunnerProvisioningService> logger;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        ApiKeyService apiKeyService,
        IHostedRunnerRemoteCommandExecutorResolver remoteCommandExecutorResolver,
        IOptionsSnapshot<HostedRunnerOptions> options,
        IAuditEventService auditEventService,
        ILogger<HostedRunnerProvisioningService> logger)
    {
        this.db = db;
        this.apiKeyService = apiKeyService;
        this.remoteCommandExecutorResolver = remoteCommandExecutorResolver;
        this.options = options.Value;
        this.auditEventService = auditEventService;
        this.logger = logger;
    }

    public async Task<HostedRunnerPreparationResult> PrepareProvisioningAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken = default,
        bool forceProvision = false)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken);
        if (plan == null)
        {
            throw new InvalidOperationException("Subscription plan not found.");
        }

        var capabilities = PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson).HostedRunner;
        if (!capabilities.Enabled)
        {
            await DisableHostedRunnersForUserAsync(userId, "Assigned plan does not include a hosted runner.", cancellationToken);
            throw new InvalidOperationException("User plan does not include managed backend runtime.");
        }

        var hostedRunner = await db.HostedRunners
            .Include(x => x.Runner)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.DeletedAt == null && x.Status != HostedRunnerStatuses.Disabled, cancellationToken);

        var now = DateTime.UtcNow;
        var createdHostedRunner = false;
        if (hostedRunner == null)
        {
            createdHostedRunner = true;
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
        hostedRunner.RuntimeEndpoint = BuildRuntimeEndpoint(hostedRunner.ContainerName);
        hostedRunner.UpdatedAt = now;

        var host = await SelectHostAsync(capabilities, cancellationToken);
        if (host == null)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = "No active runner host has enough available capacity.";
            AddDeployment(hostedRunner, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            return new HostedRunnerPreparationResult(hostedRunner, null, false);
        }

        hostedRunner.RunnerHostId = host.Id;
        hostedRunner.LastError = null;

        if (!forceProvision && hostedRunner.RunnerId != null && hostedRunner.Status == HostedRunnerStatuses.Running)
        {
            await db.SaveChangesAsync(cancellationToken);
            return new HostedRunnerPreparationResult(hostedRunner, null, false);
        }

        if (!forceProvision &&
            !createdHostedRunner &&
            (hostedRunner.Status == HostedRunnerStatuses.Provisioning || hostedRunner.Status == HostedRunnerStatuses.Pending))
        {
            await db.SaveChangesAsync(cancellationToken);
            return new HostedRunnerPreparationResult(hostedRunner, null, false);
        }

        var rawApiKey = await EnsureProvisioningApiKeyAsync(hostedRunner, userId, cancellationToken);
        hostedRunner.Status = options.Enabled
            ? HostedRunnerStatuses.Provisioning
            : HostedRunnerStatuses.Pending;

        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = AuditActions.Requested,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = AuditEventTypes.HostedRunnerProvisioningRequested,
                Message = $"Hosted runner '{hostedRunner.Name}' provisioning was requested",
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerHostId,
                    RunnerHostName = host.Name,
                    hostedRunner.SubscriptionPlanId,
                    hostedRunner.CpuCores,
                    hostedRunner.MemoryMb,
                    hostedRunner.DiskMb,
                    hostedRunner.MaxApps,
                    DeploymentMode = options.DeploymentMode,
                    IsProvisioningEnabled = options.Enabled
                }
            },
            cancellationToken);

        AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentStatuses.Running,
            HostedRunnerDockerCommandBuilder.BuildPreview(hostedRunner, host, options),
            options.Enabled
                ? "Managed runner provisioning queued."
                : "Hosted runner provisioning is disabled. Enable HostedRunners:Enabled after adding a host and validating capacity.");

        await SaveProvisioningCheckpointAsync(hostedRunner, cancellationToken);
        return new HostedRunnerPreparationResult(hostedRunner, rawApiKey, true);
    }

    public async Task<HostedRunner?> EnsureHostedRunnerForUserPlanAsync(Guid userId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, cancellationToken);
        if (plan == null)
        {
            throw new InvalidOperationException("Subscription plan not found.");
        }

        var capabilities = PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson).HostedRunner;
        if (!capabilities.Enabled)
        {
            await DisableHostedRunnersForUserAsync(userId, "Assigned plan does not include a hosted runner.", cancellationToken);
            return null;
        }

        var preparation = await PrepareProvisioningAsync(userId, planId, cancellationToken, forceProvision: true);
        var hostedRunner = preparation.HostedRunner;
        var host = hostedRunner.RunnerHostId.HasValue
            ? await db.RunnerHosts
                .Include(x => x.InfrastructureRunner)
                .FirstOrDefaultAsync(x => x.Id == hostedRunner.RunnerHostId.Value, cancellationToken)
            : null;
        if (!preparation.ShouldPublishProvisionRequest)
        {
            return hostedRunner;
        }

        if (!options.Enabled)
        {
            logger.LogInformation("Hosted runner {HostedRunnerId} prepared in dry-run mode for user {UserId}", hostedRunner.Id, userId);
            return hostedRunner;
        }

        if (host == null)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = "No runner host is assigned for hosted runner provisioning.";
            var deployment = await db.HostedRunnerDeployments
                .Where(x => x.HostedRunnerId == hostedRunner.Id && x.Action == HostedRunnerDeploymentActions.Provision)
                .OrderByDescending(x => x.StartedAt)
                .FirstAsync(cancellationToken);
            CompleteDeployment(deployment, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            await WriteProvisioningCompletionAuditAsync(
                hostedRunner,
                host?.Name,
                AuditEventTypes.HostedRunnerProvisioningFailed,
                "Failed",
                $"Hosted runner '{hostedRunner.Name}' provisioning failed",
                hostedRunner.LastError,
                cancellationToken);
            return hostedRunner;
        }

        var deploymentRecord = await db.HostedRunnerDeployments
            .Where(x => x.HostedRunnerId == hostedRunner.Id && x.Action == HostedRunnerDeploymentActions.Provision)
            .OrderByDescending(x => x.StartedAt)
            .FirstAsync(cancellationToken);

        IHostedRunnerRemoteCommandExecutor executor;
        try
        {
            executor = remoteCommandExecutorResolver.Resolve(host, options);
        }
        catch (InvalidOperationException ex)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = ex.Message;
            CompleteDeployment(deploymentRecord, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            await WriteProvisioningCompletionAuditAsync(
                hostedRunner,
                host.Name,
                AuditEventTypes.HostedRunnerProvisioningFailed,
                "Failed",
                $"Hosted runner '{hostedRunner.Name}' provisioning failed",
                hostedRunner.LastError,
                cancellationToken);
            return hostedRunner;
        }
        var provisionScript = HostedRunnerDockerCommandBuilder.BuildProvisionScript(hostedRunner, host, options, preparation.RawApiKey!);
        var executionResult = await executor.ExecuteProvisionScriptAsync(host, provisionScript, cancellationToken);
        if (!executionResult.Succeeded)
        {
            hostedRunner.Status = HostedRunnerStatuses.Failed;
            hostedRunner.LastError = BuildRemoteFailureMessage(executionResult);
            CompleteDeployment(deploymentRecord, HostedRunnerDeploymentStatuses.Failed, executionResult.StandardOutput, hostedRunner.LastError);
            await db.SaveChangesAsync(cancellationToken);
            await WriteProvisioningCompletionAuditAsync(
                hostedRunner,
                host.Name,
                AuditEventTypes.HostedRunnerProvisioningFailed,
                "Failed",
                $"Hosted runner '{hostedRunner.Name}' provisioning failed",
                $"Hosted runner provisioning failed with exit status {executionResult.ExitStatus}.",
                cancellationToken);
            return hostedRunner;
        }

        hostedRunner.ContainerId = executionResult.ContainerId;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        CompleteDeployment(
            deploymentRecord,
            HostedRunnerDeploymentStatuses.Succeeded,
            executionResult.StandardOutput,
            string.IsNullOrWhiteSpace(executionResult.StandardError) ? null : executionResult.StandardError);

        await db.SaveChangesAsync(cancellationToken);
        await WriteProvisioningCompletionAuditAsync(
            hostedRunner,
            host.Name,
            AuditEventTypes.HostedRunnerProvisioningSucceeded,
            "Success",
            $"Hosted runner '{hostedRunner.Name}' provisioning succeeded",
            null,
            cancellationToken);
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
        var requiresFreshRawKey = options.Enabled && options.DeploymentMode != HostedRunnerDeploymentModes.DryRun;
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
            ApiKeyScope.Runner,
            saveChanges: false);

        hostedRunner.ApiKeyId = apiKey.Id;
        return rawApiKey;
    }

    private HostedRunnerDeployment AddDeployment(
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

        return $"Remote provisioning command failed with exit status {result.ExitStatus}.";
    }

    private static string BuildRunnerName(Guid userId) => $"Hosted Runner {userId.ToString("N")[..8]}";

    private static string BuildContainerName(Guid userId) => $"automaton-hosted-runner-{userId.ToString("N")[..12]}";

    private string BuildRuntimeEndpoint(string containerName)
        => $"http://{containerName}:{options.RuntimePort}";

    private async Task SaveProvisioningCheckpointAsync(HostedRunner hostedRunner, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entries = ex.Entries.Select(entry =>
            {
                var keyValues = entry.Metadata.FindPrimaryKey()?.Properties
                    .Select(property => $"{property.Name}={entry.Property(property.Name).CurrentValue}")
                    ?? [];

                return $"{entry.Metadata.ClrType.Name} [{entry.State}] ({string.Join(", ", keyValues)})";
            });

            logger.LogError(
                ex,
                "Hosted runner provisioning checkpoint failed for runner {HostedRunnerId}. Concurrency entries: {Entries}",
                hostedRunner.Id,
                string.Join("; ", entries));

            throw;
        }
    }

    private async Task WriteProvisioningCompletionAuditAsync(
        HostedRunner hostedRunner,
        string? runnerHostName,
        string eventType,
        string outcome,
        string message,
        string? failureSummary,
        CancellationToken cancellationToken)
    {
        await auditEventService.WriteAsync(
            new AuditEventWriteRequest
            {
                Action = outcome == AuditOutcomes.Success ? AuditActions.Succeeded : AuditActions.Failed,
                EntityType = AuditEntityTypes.HostedRunner,
                EntityId = hostedRunner.Id,
                EventType = eventType,
                Outcome = outcome,
                Message = message,
                TargetUserId = hostedRunner.UserId,
                Metadata = new
                {
                    hostedRunner.Name,
                    hostedRunner.Status,
                    hostedRunner.RunnerId,
                    hostedRunner.RunnerHostId,
                    RunnerHostName = runnerHostName,
                    hostedRunner.SubscriptionPlanId,
                    hostedRunner.ContainerName,
                    hostedRunner.ContainerId,
                    FailureSummary = failureSummary
                }
            },
            cancellationToken);
    }
}

public sealed record HostedRunnerPreparationResult(
    HostedRunner HostedRunner,
    string? RawApiKey,
    bool ShouldPublishProvisionRequest);
