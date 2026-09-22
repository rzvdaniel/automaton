using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Storage;
using System.Net;

namespace Automaton.Api.Services;

public class HostedRunnerProvisioningService
{
    private static readonly string[] OccupyingStatuses =
    [
        HostedRunnerStatuses.Pending,
        HostedRunnerStatuses.Provisioning,
        HostedRunnerStatuses.Reprovisioning,
        HostedRunnerStatuses.Running
    ];

    private readonly ApplicationDbContext db;
    private readonly RunnerEnrollmentService runnerEnrollmentService;
    private readonly IEventPublisher eventPublisher;
    private readonly HostedRunnerOptions options;
    private readonly ISubscriptionResourcePolicyResolver resourcePolicyResolver;
    private readonly IAuditEventService auditEventService;
    private readonly ILogger<HostedRunnerProvisioningService> logger;
    private readonly StorageTelemetryOptions storageTelemetryOptions;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        RunnerEnrollmentService runnerEnrollmentService,
        IEventPublisher eventPublisher,
        IOptionsSnapshot<HostedRunnerOptions> options,
        ISubscriptionResourcePolicyResolver resourcePolicyResolver,
        IAuditEventService auditEventService,
        ILogger<HostedRunnerProvisioningService> logger,
        IOptions<StorageTelemetryOptions>? storageTelemetryOptions = null)
    {
        this.db = db;
        this.runnerEnrollmentService = runnerEnrollmentService;
        this.eventPublisher = eventPublisher;
        this.options = options.Value;
        this.resourcePolicyResolver = resourcePolicyResolver;
        this.auditEventService = auditEventService;
        this.logger = logger;
        this.storageTelemetryOptions = storageTelemetryOptions?.Value ?? new StorageTelemetryOptions();
    }

    public async Task<HostedRunnerPreparationResult> PrepareProvisioningAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken = default,
        bool forceProvision = false)
    {
        var platformSettings = await db.HostedRunnerPlatformSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? new HostedRunnerPlatformSettings { ProvisioningEnabled = true };
        if (!platformSettings.ProvisioningEnabled)
        {
            throw new InvalidOperationException("Hosted runner provisioning is paused by platform settings.");
        }
        var policyResolution = await resourcePolicyResolver.ResolveAsync(
            userId,
            planId == Guid.Empty ? null : planId,
            cancellationToken);
        if (!policyResolution.Succeeded)
        {
            var failure = policyResolution.Failure
                ?? SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured;
            var message = failure switch
            {
                SubscriptionResourcePolicyResolutionFailure.NoEffectivePlan => "Your subscription plan does not include a hosted runner.",
                SubscriptionResourcePolicyResolutionFailure.CapabilityDisabled => "Your subscription plan does not include a hosted runner.",
                SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured => "The hosted runner configuration for your subscription plan is incomplete.",
                SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound => "The hosted runner specification configured for your subscription plan no longer exists.",
                SubscriptionResourcePolicyResolutionFailure.SpecificationInactive => "The hosted runner specification configured for your subscription plan is inactive.",
                SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources => "The hosted runner specification configured for your subscription plan has invalid resource limits.",
                _ => "The hosted runner configuration for your subscription plan is invalid."
            };

            if (failure is SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured
                or SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound
                or SubscriptionResourcePolicyResolutionFailure.SpecificationInactive
                or SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources)
            {
                logger.LogError("Hosted runner specification resolution failed for user {UserId}: {Failure}", userId, failure);
            }

            throw new InvalidOperationException(message);
        }

        var specification = policyResolution.Specification!;

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

        var requestedSnapshot = HostedRunnerSpecificationSnapshotFactory.Create(specification, now);
        requestedSnapshot.Resources = policyResolution.Policy!.ApplyTo(requestedSnapshot.Resources);
        hostedRunner.StorageAllowanceBytes = requestedSnapshot.Resources.DiskLimitMb * 1024L * 1024L;
        var storageProjectWasMissing = hostedRunner.StorageProjectId is null;
        if (hostedRunner.StorageProjectId is null)
        {
            hostedRunner.StorageProjectId = await AllocateStorageProjectIdAsync(hostedRunner.Id, cancellationToken);
            if (!createdHostedRunner)
                await db.SaveChangesAsync(cancellationToken);
        }
        if (!forceProvision && !storageProjectWasMissing && hostedRunner is { RunnerId: not null, Status: HostedRunnerStatuses.Running })
        {
            return new HostedRunnerPreparationResult(hostedRunner, [], false);
        }

        if (!forceProvision && !storageProjectWasMissing && !createdHostedRunner && hostedRunner.Status is
            HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning)
        {
            return new HostedRunnerPreparationResult(hostedRunner, [], false);
        }

        var previousHostId = hostedRunner.RunnerHostId;
        if (createdHostedRunner)
        {
            hostedRunner.HostedRunnerSpecificationId = specification.Id;
            hostedRunner.SpecificationRevision = specification.Revision;
            hostedRunner.SpecificationSnapshot = requestedSnapshot;
        }
        hostedRunner.UpdatedAt = now;

        IDbContextTransaction? capacityTransaction = null;
        if (db.Database.IsRelational())
        {
            capacityTransaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        }

        try
        {
            var host = await SelectHostAsync(
                requestedSnapshot.Resources,
                createdHostedRunner ? null : hostedRunner.Id,
                cancellationToken);
            if (host == null)
            {
                hostedRunner.Status = HostedRunnerStatuses.Failed;
                hostedRunner.LastError = "No active runner host has enough available capacity.";
                AddDeployment(hostedRunner, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
                await db.SaveChangesAsync(cancellationToken);
                if (capacityTransaction != null)
                {
                    await capacityTransaction.CommitAsync(cancellationToken);
                }

                return new HostedRunnerPreparationResult(hostedRunner, [], false);
            }

            hostedRunner.RunnerHostId = host.Id;
            hostedRunner.RuntimePort = await AllocateRuntimePortAsync(
                host,
                hostedRunner,
                previousHostId == host.Id,
                cancellationToken);
            hostedRunner.RuntimeEndpoint = BuildRuntimeEndpoint(hostedRunner, host);
            hostedRunner.LastError = null;

            if (!createdHostedRunner)
            {
                hostedRunner.PendingSpecificationSnapshot = requestedSnapshot;
                hostedRunner.Status = HostedRunnerStatuses.Reprovisioning;
            }

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
                    hostedRunner.HostedRunnerSpecificationId,
                    hostedRunner.SpecificationRevision,
                    Resources = requestedSnapshot.Resources,
                    IsProvisioningEnabled = options.Enabled
                }
            },
            cancellationToken);

        var deployment = AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentStatuses.Queued,
            $"Trusted infrastructure Action: {(createdHostedRunner ? "provision-hosted-runner" : "reprovision-hosted-runner")} on Runner Host '{host.Name}'.",
            options.Enabled
                ? "Managed runner provisioning queued."
                : "Hosted runner provisioning is disabled. Enable HostedRunners:Enabled after adding a host and validating capacity.",
            createdHostedRunner ? HostedRunnerDeploymentActions.Provision : HostedRunnerDeploymentActions.Reprovision);

            await SaveProvisioningCheckpointAsync(hostedRunner, cancellationToken);
            IReadOnlyList<Guid> secretGrantIds = [];
            if (options.Enabled)
            {
                var infrastructureRunnerId = host.InfrastructureRunnerId
                    ?? throw new InvalidOperationException("The selected Runner Host has no Infrastructure Runner assigned.");
                var actionKey = createdHostedRunner ? "provision-hosted-runner" : "reprovision-hosted-runner";
                var enrollment = await runnerEnrollmentService.CreateHostedRunnerAsync(
                    hostedRunner.Id,
                    deployment.Id,
                    actionKey,
                    infrastructureRunnerId,
                    cancellationToken);
                secretGrantIds = [enrollment.SecretGrantId];
                deployment.RegistrationDeadlineUtc = enrollment.ExpiresAtUtc;
                await db.SaveChangesAsync(cancellationToken);
            }
            if (capacityTransaction != null)
            {
                await capacityTransaction.CommitAsync(cancellationToken);
            }

            return new HostedRunnerPreparationResult(hostedRunner, secretGrantIds, true, deployment.Id);
        }
        finally
        {
            if (capacityTransaction != null)
            {
                await capacityTransaction.DisposeAsync();
            }
        }
    }

    public async Task<HostedRunner?> EnsureHostedRunnerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareProvisioningAsync(userId, Guid.Empty, cancellationToken, forceProvision: true);
        var hostedRunner = preparation.HostedRunner;
        if (!preparation.ShouldPublishProvisionRequest)
        {
            return hostedRunner.Status == HostedRunnerStatuses.Failed ? null : hostedRunner;
        }

        if (!options.Enabled)
        {
            logger.LogInformation("Hosted runner {HostedRunnerId} prepared in dry-run mode for user {UserId}", hostedRunner.Id, userId);
            return hostedRunner;
        }

        await eventPublisher.PublishAsync(new ManagedRunnerProvisionRequestedEvent
        {
            OperationId = preparation.OperationId,
            HostedRunnerId = hostedRunner.Id,
            UserId = hostedRunner.UserId,
            RunnerHostId = hostedRunner.RunnerHostId ?? Guid.Empty,
            SecretGrantIds = preparation.SecretGrantIds
        }, cancellationToken);
        logger.LogInformation("Hosted runner {HostedRunnerId} provisioning queued through the trusted infrastructure Action", hostedRunner.Id);
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

    private async Task<RunnerHost?> SelectHostAsync(
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId,
        CancellationToken cancellationToken)
    {
        var requestedStorageBytes = requested.DiskLimitMb * 1024L * 1024L;
        var now = DateTime.UtcNow;
        var hosts = (await db.RunnerHosts
            .AsNoTracking()
            .Where(x => x.Status == RunnerHostStatuses.Active
                        && x.HostedRunnerSchedulingEnabled
                        && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                        && x.ValidatedAtUtc != null
                        && x.ApprovedAtUtc != null
                        && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                        && x.InfrastructureRunnerId != null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken))
            .Where(x => StorageTelemetryPolicy.CanScheduleHostedRunners(
                x.StorageState, x.StorageAvailableBytes, x.StorageReserveRequiredBytes ?? long.MaxValue, requestedStorageBytes,
                x.CreatedAt, x.StorageMeasuredAtUtc, now, storageTelemetryOptions))
            .ToList();

        foreach (var candidate in hosts)
        {
            if (!UsesLocalDockerNetwork(candidate) && string.IsNullOrWhiteSpace(candidate.PublicIp))
            {
                continue;
            }

            if (IsMySqlProvider())
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT Id FROM RunnerHosts WHERE Id = {candidate.Id} FOR UPDATE",
                    cancellationToken);
            }

            var host = await db.RunnerHosts
                .Include(x => x.HostedRunners)
                .FirstOrDefaultAsync(
                    x => x.Id == candidate.Id
                         && x.Status == RunnerHostStatuses.Active
                         && x.HostedRunnerSchedulingEnabled
                         && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                         && x.ValidatedAtUtc != null
                         && x.ApprovedAtUtc != null
                         && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                         && x.InfrastructureRunnerId != null,
                    cancellationToken);
            if (host == null)
            {
                continue;
            }
            if (!StorageTelemetryPolicy.CanScheduleHostedRunners(
                    host.StorageState, host.StorageAvailableBytes, host.StorageReserveRequiredBytes ?? long.MaxValue, requestedStorageBytes,
                    host.CreatedAt, host.StorageMeasuredAtUtc, now, storageTelemetryOptions))
            {
                continue;
            }

            var assigned = host.HostedRunners.Where(r =>
                r.Id != excludedHostedRunnerId &&
                (OccupyingStatuses.Contains(r.Status)
                    || r.Status == HostedRunnerStatuses.Failed && r.ContainerId != null) &&
                r.DeletedAt == null);
            if (host.ReservedCpuCores + assigned.Sum(r => r.SpecificationSnapshot.Resources.CpuLimit) + requested.CpuLimit <= host.TotalCpuCores
                && host.ReservedMemoryMb + assigned.Sum(r => r.SpecificationSnapshot.Resources.MemoryLimitMb) + requested.MemoryLimitMb <= host.TotalMemoryMb
                && host.ReservedDiskMb + assigned.Sum(r => r.SpecificationSnapshot.Resources.DiskLimitMb) + requested.DiskLimitMb <= host.TotalDiskMb)
            {
                return host;
            }
        }

        return null;
    }

    private bool IsMySqlProvider()
        => db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<int> AllocateStorageProjectIdAsync(Guid hostedRunnerId, CancellationToken cancellationToken)
    {
        // XFS project IDs are persisted in the control plane. The hash gives a
        // stable first candidate, while linear probing plus the unique index
        // makes collisions explicit instead of silently sharing a project.
        var digest = SHA256.HashData(hostedRunnerId.ToByteArray());
        var candidate = 1_000_000 + (int)(BitConverter.ToUInt32(digest, 0) % (uint)(int.MaxValue - 1_000_000));
        var used = await db.HostedRunners
            .Where(x => x.StorageProjectId != null)
            .Select(x => x.StorageProjectId!.Value)
            .ToListAsync(cancellationToken);
        var usedIds = used.ToHashSet();
        while (usedIds.Contains(candidate))
        {
            candidate++;
            if (candidate == int.MaxValue) candidate = 1_000_000;
        }
        return candidate;
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
            FinishedAt = status is HostedRunnerDeploymentStatuses.Failed or HostedRunnerDeploymentStatuses.Skipped
                ? DateTime.UtcNow
                : null
        };
        db.HostedRunnerDeployments.Add(deployment);
        return deployment;
    }

    private static string BuildRunnerName(Guid userId) => $"Hosted Runner {userId.ToString("N")[..8]}";

    private static string BuildContainerName(Guid userId) => $"automaton-hosted-runner-{userId.ToString("N")[..12]}";

    private string? BuildRuntimeEndpoint(HostedRunner hostedRunner, RunnerHost host)
    {
        if (UsesLocalDockerNetwork(host))
        {
            return $"http://{hostedRunner.ContainerName}:{options.RuntimePort}";
        }

        if (string.IsNullOrWhiteSpace(host.PublicIp) || hostedRunner.RuntimePort is not > 0)
        {
            return null;
        }

        var endpointHost = host.PublicIp.Trim();
        if (IPAddress.TryParse(endpointHost, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            endpointHost = $"[{endpointHost}]";
        }

        return $"http://{endpointHost}:{hostedRunner.RuntimePort}";
    }

    private async Task<int?> AllocateRuntimePortAsync(
        RunnerHost host,
        HostedRunner hostedRunner,
        bool keepExistingPort,
        CancellationToken cancellationToken)
    {
        if (UsesLocalDockerNetwork(host))
        {
            return null;
        }

        if (keepExistingPort && hostedRunner.RuntimePort is > 0)
        {
            return hostedRunner.RuntimePort;
        }

        var start = options.RemoteRuntimePortStart;
        var end = options.RemoteRuntimePortEnd;
        if (start <= 0 || end < start || end > 65535)
        {
            throw new InvalidOperationException("HostedRunners remote runtime port range is invalid.");
        }

        var usedPorts = await db.HostedRunners
            .Where(x => x.RunnerHostId == host.Id &&
                        x.Id != hostedRunner.Id &&
                        x.DeletedAt == null &&
                        x.RuntimePort != null)
            .Select(x => x.RuntimePort!.Value)
            .ToListAsync(cancellationToken);
        var used = usedPorts.ToHashSet();

        for (var port = start; port <= end; port++)
        {
            if (!used.Contains(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"Runner host '{host.Name}' has no available remote runtime ports.");
    }

    private static bool UsesLocalDockerNetwork(RunnerHost host)
        => string.Equals(
            host.RuntimeConnectivityMode,
            RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            StringComparison.Ordinal);

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
                    hostedRunner.HostedRunnerSpecificationId,
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
    IReadOnlyList<Guid> SecretGrantIds,
    bool ShouldPublishProvisionRequest,
    Guid OperationId = default);
