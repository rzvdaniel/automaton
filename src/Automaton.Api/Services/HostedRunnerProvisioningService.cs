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

namespace Automaton.Api.Services;

public class HostedRunnerProvisioningService
{
    private readonly ApplicationDbContext db;
    private readonly RunnerEnrollmentService runnerEnrollmentService;
    private readonly IEventPublisher eventPublisher;
    private readonly HostedRunnerOptions options;
    private readonly ISubscriptionResourcePolicyResolver resourcePolicyResolver;
    private readonly IAccountStorageProjectAllocationService storageProjectAllocationService;
    private readonly IAuditEventService auditEventService;
    private readonly ILogger<HostedRunnerProvisioningService> logger;
    private readonly HostedRunnerHostPlacementService hostPlacementService;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        RunnerEnrollmentService runnerEnrollmentService,
        IEventPublisher eventPublisher,
        IOptionsSnapshot<HostedRunnerOptions> options,
        ISubscriptionResourcePolicyResolver resourcePolicyResolver,
        IAccountStorageProjectAllocationService storageProjectAllocationService,
        IAuditEventService auditEventService,
        ILogger<HostedRunnerProvisioningService> logger,
        HostedRunnerHostPlacementService hostPlacementService)
    {
        this.db = db;
        this.runnerEnrollmentService = runnerEnrollmentService;
        this.eventPublisher = eventPublisher;
        this.options = options.Value;
        this.resourcePolicyResolver = resourcePolicyResolver;
        this.storageProjectAllocationService = storageProjectAllocationService;
        this.auditEventService = auditEventService;
        this.logger = logger;
        this.hostPlacementService = hostPlacementService;
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
            var hostSelection = await hostPlacementService.SelectHostAsync(
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
            hostedRunner.RuntimePort = await hostPlacementService.AllocateRuntimePortAsync(
                host,
                hostedRunner,
                previousHostId == host.Id,
                cancellationToken);
            hostedRunner.RuntimeEndpoint = hostPlacementService.BuildRuntimeEndpoint(hostedRunner, host);
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
