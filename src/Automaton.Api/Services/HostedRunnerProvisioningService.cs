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
    private readonly IAccountStorageProjectAllocationService storageProjectAllocationService;
    private readonly IAuditEventService auditEventService;
    private readonly ILogger<HostedRunnerProvisioningService> logger;
    private readonly StorageTelemetryOptions storageTelemetryOptions;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        RunnerEnrollmentService runnerEnrollmentService,
        IEventPublisher eventPublisher,
        IOptionsSnapshot<HostedRunnerOptions> options,
        ISubscriptionResourcePolicyResolver resourcePolicyResolver,
        IAccountStorageProjectAllocationService storageProjectAllocationService,
        IAuditEventService auditEventService,
        ILogger<HostedRunnerProvisioningService> logger,
        IOptions<StorageTelemetryOptions>? storageTelemetryOptions = null)
    {
        this.db = db;
        this.runnerEnrollmentService = runnerEnrollmentService;
        this.eventPublisher = eventPublisher;
        this.options = options.Value;
        this.resourcePolicyResolver = resourcePolicyResolver;
        this.storageProjectAllocationService = storageProjectAllocationService;
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
        var policyResolution = await ResolveProvisioningPolicyAsync(userId, planId, cancellationToken);
        var specification = policyResolution.Specification!;
        var accountStorageProjectId = await storageProjectAllocationService.GetOrCreateAsync(userId, cancellationToken);

        var hostedRunner = await db.HostedRunners
            .Include(x => x.Runner)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.DeletedAt == null && x.Status != HostedRunnerStatuses.Disabled, cancellationToken);

        var now = DateTime.UtcNow;
        var createdHostedRunner = hostedRunner == null;
        hostedRunner ??= CreateHostedRunner(userId, now);
        if (createdHostedRunner)
            db.HostedRunners.Add(hostedRunner);

        var requestedSnapshot = HostedRunnerSpecificationSnapshotFactory.Create(specification, now);
        requestedSnapshot.Resources = policyResolution.Policy!.ApplyTo(requestedSnapshot.Resources);
        hostedRunner.StorageAllowanceBytes = requestedSnapshot.Resources.DiskLimitMb * 1024L * 1024L;
        var storageProjectWasMissing = hostedRunner.StorageProjectId is null;
        await AssignAccountStorageProjectAsync(hostedRunner, accountStorageProjectId, createdHostedRunner, cancellationToken);

        if (CanSkipProvisioning(hostedRunner, forceProvision, storageProjectWasMissing, createdHostedRunner))
            return new HostedRunnerPreparationResult(hostedRunner, [], false);

        var previousHostId = hostedRunner.RunnerHostId;
        if (createdHostedRunner)
        {
            hostedRunner.HostedRunnerSpecificationId = specification.Id;
            hostedRunner.SpecificationRevision = specification.Revision;
            hostedRunner.SpecificationSnapshot = requestedSnapshot;
        }
        hostedRunner.UpdatedAt = now;

        return await PrepareOnSelectedHostAsync(
            userId, hostedRunner, requestedSnapshot, accountStorageProjectId, createdHostedRunner,
            previousHostId, now, cancellationToken);
    }

    private async Task<SubscriptionResourcePolicyResolution> ResolveProvisioningPolicyAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken)
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
            ThrowPolicyResolutionFailure(userId, policyResolution.Failure);

        return policyResolution;
    }

    private void ThrowPolicyResolutionFailure(
        Guid userId,
        SubscriptionResourcePolicyResolutionFailure? policyFailure)
    {
        var failure = policyFailure ?? SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured;
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

    private static HostedRunner CreateHostedRunner(Guid userId, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            Name = BuildRunnerName(userId),
            ContainerName = BuildContainerName(userId)
        };

    private async Task AssignAccountStorageProjectAsync(
        HostedRunner hostedRunner,
        int accountStorageProjectId,
        bool createdHostedRunner,
        CancellationToken cancellationToken)
    {
        if (hostedRunner.StorageProjectId == accountStorageProjectId)
            return;

        hostedRunner.StorageProjectId = accountStorageProjectId;
        if (!createdHostedRunner)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static bool CanSkipProvisioning(
        HostedRunner hostedRunner,
        bool forceProvision,
        bool storageProjectWasMissing,
        bool createdHostedRunner)
        => !forceProvision && !storageProjectWasMissing
           && (hostedRunner is { RunnerId: not null, Status: HostedRunnerStatuses.Running }
               || (!createdHostedRunner
                   && hostedRunner.Status is (HostedRunnerStatuses.Pending or HostedRunnerStatuses.Provisioning)));

    private async Task<HostedRunnerPreparationResult> PrepareOnSelectedHostAsync(
        Guid userId,
        HostedRunner hostedRunner,
        HostedRunnerSpecificationSnapshot requestedSnapshot,
        int accountStorageProjectId,
        bool createdHostedRunner,
        Guid? previousHostId,
        DateTime now,
        CancellationToken cancellationToken)
    {

        IDbContextTransaction? capacityTransaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction == null)
        {
            capacityTransaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        }

        try
        {
            var hostSelection = await SelectHostAsync(
                userId,
                requestedSnapshot.Resources,
                createdHostedRunner ? null : hostedRunner.Id,
                accountStorageProjectId,
                cancellationToken);
            var host = hostSelection.Host;
            if (host == null)
            {
                var failedPreparation = await RecordNoHostAvailableAsync(hostedRunner, hostSelection.FailureReason, cancellationToken);
                await CommitCapacityTransactionAsync(capacityTransaction, cancellationToken);
                return failedPreparation;
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

            var deployment = await CreateProvisioningDeploymentAsync(
                hostedRunner, host, requestedSnapshot, createdHostedRunner, cancellationToken);

            await SaveProvisioningCheckpointAsync(hostedRunner, cancellationToken);
            var secretGrantIds = await CreateEnrollmentMaterialAsync(
                hostedRunner, host, deployment, createdHostedRunner, cancellationToken);
            await CommitCapacityTransactionAsync(capacityTransaction, cancellationToken);

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

    private async Task<HostedRunnerPreparationResult> RecordNoHostAvailableAsync(
        HostedRunner hostedRunner,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        hostedRunner.Status = HostedRunnerStatuses.Failed;
        hostedRunner.LastError = failureReason ?? "No active runner host has enough available capacity.";
        AddDeployment(hostedRunner, HostedRunnerDeploymentStatuses.Failed, null, hostedRunner.LastError);
        await db.SaveChangesAsync(cancellationToken);
        return new HostedRunnerPreparationResult(hostedRunner, [], false);
    }

    private async Task<HostedRunnerDeployment> CreateProvisioningDeploymentAsync(
        HostedRunner hostedRunner,
        RunnerHost host,
        HostedRunnerSpecificationSnapshot requestedSnapshot,
        bool createdHostedRunner,
        CancellationToken cancellationToken)
    {
        await auditEventService.WriteAsync(new AuditEventWriteRequest
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
        }, cancellationToken);

        return AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentStatuses.Queued,
            $"Trusted infrastructure Action: {(createdHostedRunner ? "provision-hosted-runner" : "reprovision-hosted-runner")} on Runner Host '{host.Name}'.",
            options.Enabled
                ? "Managed runner provisioning queued."
                : "Hosted runner provisioning is disabled. Enable HostedRunners:Enabled after adding a host and validating capacity.",
            createdHostedRunner ? HostedRunnerDeploymentActions.Provision : HostedRunnerDeploymentActions.Reprovision);
    }

    private async Task<IReadOnlyList<Guid>> CreateEnrollmentMaterialAsync(
        HostedRunner hostedRunner,
        RunnerHost host,
        HostedRunnerDeployment deployment,
        bool createdHostedRunner,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return [];

        var infrastructureRunnerId = host.InfrastructureRunnerId
            ?? throw new InvalidOperationException("The selected Runner Host has no Infrastructure Runner assigned.");
        var actionKey = createdHostedRunner ? "provision-hosted-runner" : "reprovision-hosted-runner";
        var enrollment = await runnerEnrollmentService.CreateHostedRunnerAsync(
            hostedRunner.Id, deployment.Id, actionKey, infrastructureRunnerId, cancellationToken);
        deployment.RegistrationDeadlineUtc = enrollment.ExpiresAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        return [enrollment.SecretGrantId];
    }

    private static async Task CommitCapacityTransactionAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
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

    private async Task<(RunnerHost? Host, string? FailureReason)> SelectHostAsync(
        Guid accountId,
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId,
        CancellationToken cancellationToken)
    {
        var hasSharedProjectLimitConflict = false;
        var requestedStorageBytes = requested.DiskLimitMb * 1024L * 1024L;
        var now = DateTime.UtcNow;
        var hosts = await GetSchedulableHostsAsync(requestedStorageBytes, now, cancellationToken);

        foreach (var candidate in hosts)
        {
            var selection = await TrySelectHostAsync(
                candidate, accountId, requested, excludedHostedRunnerId, accountStorageProjectId,
                requestedStorageBytes, now, cancellationToken);
            if (selection.Host != null)
                return (selection.Host, null);
            hasSharedProjectLimitConflict |= selection.SharedProjectLimitConflict;
        }

        if (hasSharedProjectLimitConflict)
        {
            return (null,
                "The account already has a Hosted Runner on an eligible host using the same XFS project with a different disk-limit snapshot. " +
                "Move or delete the co-located Hosted Runner before changing the shared project limit.");
        }

        return (null, null);
    }

    private async Task<List<RunnerHost>> GetSchedulableHostsAsync(
        long requestedStorageBytes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = await db.RunnerHosts
            .AsNoTracking()
            .Where(x => x.Status == RunnerHostStatuses.Active
                        && x.HostedRunnerSchedulingEnabled
                        && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                        && x.ValidatedAtUtc != null
                        && x.ApprovedAtUtc != null
                        && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                        && x.InfrastructureRunnerId != null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return candidates.Where(host => StorageTelemetryPolicy.CanScheduleHostedRunners(
                host.StorageState, host.StorageAvailableBytes, host.StorageReserveRequiredBytes ?? long.MaxValue,
                requestedStorageBytes, host.CreatedAt, host.StorageMeasuredAtUtc, now, storageTelemetryOptions))
            .ToList();
    }

    private async Task<(RunnerHost? Host, bool SharedProjectLimitConflict)> TrySelectHostAsync(
        RunnerHost candidate,
        Guid accountId,
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId,
        long requestedStorageBytes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!UsesLocalDockerNetwork(candidate) && string.IsNullOrWhiteSpace(candidate.PublicIp))
            return (null, false);

        if (IsMySqlProvider())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM RunnerHosts WHERE Id = {candidate.Id} FOR UPDATE",
                cancellationToken);
        }

        var host = await LoadEligibleHostAsync(candidate.Id, cancellationToken);
        if (host == null || !CanScheduleStorage(host, requestedStorageBytes, now))
            return (null, false);

        if (HasIncompatibleSharedProjectLimit(host, accountId, requested.DiskLimitMb, excludedHostedRunnerId, accountStorageProjectId))
            return (null, true);

        return HasCapacity(host, requested, excludedHostedRunnerId)
            ? (host, false)
            : (null, false);
    }

    private Task<RunnerHost?> LoadEligibleHostAsync(Guid hostId, CancellationToken cancellationToken)
        => db.RunnerHosts
            .Include(x => x.HostedRunners)
            .FirstOrDefaultAsync(
                x => x.Id == hostId
                     && x.Status == RunnerHostStatuses.Active
                     && x.HostedRunnerSchedulingEnabled
                     && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                     && x.ValidatedAtUtc != null
                     && x.ApprovedAtUtc != null
                     && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                     && x.InfrastructureRunnerId != null,
                cancellationToken);

    private bool CanScheduleStorage(RunnerHost host, long requestedStorageBytes, DateTime now)
        => StorageTelemetryPolicy.CanScheduleHostedRunners(
            host.StorageState, host.StorageAvailableBytes, host.StorageReserveRequiredBytes ?? long.MaxValue,
            requestedStorageBytes, host.CreatedAt, host.StorageMeasuredAtUtc, now, storageTelemetryOptions);

    private static bool HasIncompatibleSharedProjectLimit(
        RunnerHost host,
        Guid accountId,
        int requestedDiskLimitMb,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId)
        => host.HostedRunners.Any(runner =>
            runner.UserId == accountId
            && runner.Id != excludedHostedRunnerId
            && runner.DeletedAt == null
            && runner.StorageProjectId == accountStorageProjectId
            && (runner.PendingSpecificationSnapshot ?? runner.SpecificationSnapshot).Resources.DiskLimitMb != requestedDiskLimitMb);

    private static bool HasCapacity(RunnerHost host, HostedRunnerResources requested, Guid? excludedHostedRunnerId)
    {
        var assigned = host.HostedRunners.Where(runner =>
            runner.Id != excludedHostedRunnerId
            && (OccupyingStatuses.Contains(runner.Status)
                || runner.Status == HostedRunnerStatuses.Failed && runner.ContainerId != null)
            && runner.DeletedAt == null);
        return host.ReservedCpuCores + assigned.Sum(runner => runner.SpecificationSnapshot.Resources.CpuLimit) + requested.CpuLimit <= host.TotalCpuCores
            && host.ReservedMemoryMb + assigned.Sum(runner => runner.SpecificationSnapshot.Resources.MemoryLimitMb) + requested.MemoryLimitMb <= host.TotalMemoryMb
            && host.ReservedDiskMb + assigned.Sum(runner => runner.SpecificationSnapshot.Resources.DiskLimitMb) + requested.DiskLimitMb <= host.TotalDiskMb;
    }

    private bool IsMySqlProvider()
        => db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

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
