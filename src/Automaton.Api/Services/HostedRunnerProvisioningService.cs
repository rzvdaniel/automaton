using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Data;

namespace Automaton.Api.Services;

public class HostedRunnerProvisioningService
{
    private readonly ApplicationDbContext db;
    private readonly IEventPublisher eventPublisher;
    private readonly HostedRunnerOptions options;
    private readonly HostedRunnerProvisioningPolicyService provisioningPolicyService;
    private readonly IAccountStorageProjectAllocationService storageProjectAllocationService;
    private readonly HostedRunnerStorageLocalityResolver storageLocalityResolver;
    private readonly ILogger<HostedRunnerProvisioningService> logger;
    private readonly HostedRunnerProvisioningPreparationService preparationService;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        IEventPublisher eventPublisher,
        IOptionsSnapshot<HostedRunnerOptions> options,
        HostedRunnerProvisioningPolicyService provisioningPolicyService,
        IAccountStorageProjectAllocationService storageProjectAllocationService,
        HostedRunnerStorageLocalityResolver storageLocalityResolver,
        ILogger<HostedRunnerProvisioningService> logger,
        HostedRunnerProvisioningPreparationService preparationService)
    {
        this.db = db;
        this.eventPublisher = eventPublisher;
        this.options = options.Value;
        this.provisioningPolicyService = provisioningPolicyService;
        this.storageProjectAllocationService = storageProjectAllocationService;
        this.storageLocalityResolver = storageLocalityResolver;
        this.logger = logger;
        this.preparationService = preparationService;
    }

    public async Task<HostedRunnerPreparationResult> PrepareProvisioningAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken = default,
        bool forceProvision = false,
        Guid? requiredHostedRunnerId = null)
    {
        var policyResolution = await provisioningPolicyService.ResolveAsync(userId, planId, cancellationToken);
        var specification = policyResolution.Specification!;
        var policy = policyResolution.Policy!;
        if (policy.MaxHostedRunners <= 0)
            throw new InvalidOperationException("The effective subscription policy does not allow a Hosted Runner.");

        var admissionTransaction = await BeginAdmissionTransactionAsync(cancellationToken);
        try
        {
            await LockAccountAdmissionAsync(userId, cancellationToken);
            var storageAllocation = await storageProjectAllocationService.GetOrCreateAllocationAsync(userId, cancellationToken);
            var preparation = await PrepareUnderAdmissionLockAsync(
                userId, specification, policy, storageAllocation, forceProvision, requiredHostedRunnerId, cancellationToken);
            await CommitAdmissionTransactionAsync(admissionTransaction, cancellationToken);
            return preparation;
        }
        finally
        {
            if (admissionTransaction != null)
                await admissionTransaction.DisposeAsync();
        }
    }

    private async Task<HostedRunnerPreparationResult> PrepareUnderAdmissionLockAsync(
        Guid userId,
        HostedRunnerSpecification specification,
        SubscriptionResourcePolicy policy,
        AccountStorageAllocation storageAllocation,
        bool forceProvision,
        Guid? requiredHostedRunnerId,
        CancellationToken cancellationToken)
    {
        var storageRunnerHostId = await storageLocalityResolver.ResolveAsync(userId, storageAllocation, cancellationToken);
        var hostedRunner = await FindReusableHostedRunnerAsync(
            userId, policy.MaxHostedRunners, requiredHostedRunnerId, cancellationToken);
        EnsureRunnerMatchesStorageLocality(userId, hostedRunner, storageRunnerHostId);
        storageRunnerHostId ??= hostedRunner?.RunnerHostId;
        if (storageAllocation.StorageRunnerHostId is null && storageRunnerHostId.HasValue)
            storageAllocation.StorageRunnerHostId = storageRunnerHostId;

        var now = DateTime.UtcNow;
        var createdHostedRunner = hostedRunner == null;
        hostedRunner ??= CreateHostedRunner(userId, now);
        if (createdHostedRunner)
            db.HostedRunners.Add(hostedRunner);

        var requestedSnapshot = CreateRequestedSnapshot(specification, policy, now);
        hostedRunner.StorageAllowanceBytes = requestedSnapshot.Resources.DiskLimitMb * 1024L * 1024L;
        var storageProjectWasMissing = hostedRunner.StorageProjectId is null;
        await AssignAccountStorageProjectAsync(
            hostedRunner, storageAllocation.StorageProjectId, createdHostedRunner, cancellationToken);

        if (CanSkipProvisioning(hostedRunner, forceProvision, storageProjectWasMissing, createdHostedRunner))
        {
            await db.SaveChangesAsync(cancellationToken);
            return new HostedRunnerPreparationResult(hostedRunner, [], false);
        }

        PrepareNewRunnerSnapshot(hostedRunner, specification, requestedSnapshot, createdHostedRunner, now);
        var preparation = await preparationService.PrepareOnSelectedHostAsync(
            userId, hostedRunner, requestedSnapshot, storageAllocation.StorageProjectId, createdHostedRunner,
            hostedRunner.RunnerHostId, storageRunnerHostId, cancellationToken);
        await PersistSelectedStorageHostAsync(hostedRunner, storageAllocation, storageRunnerHostId, cancellationToken);
        return preparation;
    }

    private static void EnsureRunnerMatchesStorageLocality(
        Guid userId,
        HostedRunner? hostedRunner,
        Guid? storageRunnerHostId)
    {
        if (hostedRunner?.RunnerHostId is not Guid existingRunnerHostId
            || storageRunnerHostId is not Guid allocatedRunnerHostId
            || existingRunnerHostId == allocatedRunnerHostId)
            return;

        throw new InvalidOperationException(
            $"Account '{userId}' Hosted Runner {hostedRunner.Id} is assigned to Runner Host {existingRunnerHostId}, " +
            $"but account storage is pinned to Runner Host {allocatedRunnerHostId}. Storage must be migrated explicitly before reprovisioning.");
    }

    private static HostedRunnerSpecificationSnapshot CreateRequestedSnapshot(
        HostedRunnerSpecification specification,
        SubscriptionResourcePolicy policy,
        DateTime now)
    {
        var snapshot = HostedRunnerSpecificationSnapshotFactory.Create(specification, now);
        snapshot.Resources = policy.ApplyTo(snapshot.Resources);
        return snapshot;
    }

    private static void PrepareNewRunnerSnapshot(
        HostedRunner hostedRunner,
        HostedRunnerSpecification specification,
        HostedRunnerSpecificationSnapshot requestedSnapshot,
        bool createdHostedRunner,
        DateTime now)
    {
        if (createdHostedRunner)
        {
            hostedRunner.HostedRunnerSpecificationId = specification.Id;
            hostedRunner.SpecificationRevision = specification.Revision;
            hostedRunner.SpecificationSnapshot = requestedSnapshot;
        }

        hostedRunner.UpdatedAt = now;
    }

    private async Task PersistSelectedStorageHostAsync(
        HostedRunner hostedRunner,
        AccountStorageAllocation storageAllocation,
        Guid? requiredStorageHostId,
        CancellationToken cancellationToken)
    {
        if (hostedRunner.RunnerHostId is not Guid selectedRunnerHostId)
            return;
        if (requiredStorageHostId.HasValue && selectedRunnerHostId != requiredStorageHostId.Value)
        {
            throw new InvalidOperationException(
                $"Provisioning selected Runner Host {selectedRunnerHostId} for account storage pinned to Runner Host {requiredStorageHostId.Value}.");
        }

        storageAllocation.StorageRunnerHostId = selectedRunnerHostId;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<HostedRunner?> FindReusableHostedRunnerAsync(
        Guid userId,
        int maximumHostedRunners,
        Guid? requiredHostedRunnerId,
        CancellationToken cancellationToken)
    {
        var hostedRunners = await db.HostedRunners
            .Include(x => x.Runner)
            .Where(x => x.UserId == userId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (hostedRunners.Count > maximumHostedRunners)
        {
            var conflictingRunnerIds = string.Join(", ", hostedRunners.Select(x => x.Id));
            logger.LogError(
                "Hosted Runner admission blocked for account {AccountId}: {PersistedRunnerCount} non-deleted runners exceed the limit of {MaximumHostedRunners}. Existing runners: {HostedRunnerIds}",
                userId,
                hostedRunners.Count,
                maximumHostedRunners,
                conflictingRunnerIds);
            throw new InvalidOperationException(
                $"Account '{userId}' has {hostedRunners.Count} non-deleted Hosted Runners but the subscription allows {maximumHostedRunners}. Existing runners ({conflictingRunnerIds}) were preserved; an administrator must resolve this conflict before provisioning.");
        }

        var reusableRunner = requiredHostedRunnerId.HasValue
            ? hostedRunners.SingleOrDefault(x => x.Id == requiredHostedRunnerId.Value)
            : hostedRunners.FirstOrDefault();
        if (requiredHostedRunnerId.HasValue && reusableRunner is null)
            throw new InvalidOperationException("The selected Hosted Runner is no longer eligible for reprovisioning.");
        return reusableRunner;
    }

    private async Task<IDbContextTransaction?> BeginAdmissionTransactionAsync(CancellationToken cancellationToken)
        => db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private async Task LockAccountAdmissionAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()
            || db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM AspNetUsers WHERE Id = {userId} FOR UPDATE",
            cancellationToken);
    }

    private static Task CommitAdmissionTransactionAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
        => transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

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

    public async Task<HostedRunner?> EnsureHostedRunnerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareProvisioningAsync(userId, Guid.Empty, cancellationToken, forceProvision: true);
        return await DispatchPreparedProvisioningAsync(preparation, cancellationToken);
    }

    public async Task<HostedRunner?> DispatchPreparedProvisioningAsync(
        HostedRunnerPreparationResult preparation,
        CancellationToken cancellationToken)
    {
        var hostedRunner = preparation.HostedRunner;
        if (!preparation.ShouldPublishProvisionRequest)
        {
            return hostedRunner.Status == HostedRunnerStatuses.Failed ? null : hostedRunner;
        }

        if (!options.Enabled)
        {
            logger.LogInformation("Hosted runner {HostedRunnerId} prepared in dry-run mode for user {UserId}", hostedRunner.Id, hostedRunner.UserId);
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
        => await preparationService.DisableHostedRunnersForUserAsync(userId, reason, cancellationToken);

    private static string BuildRunnerName(Guid userId) => $"Hosted Runner {userId.ToString("N")[..8]}";

    private static string BuildContainerName(Guid userId) => $"automaton-hosted-runner-{userId.ToString("N")[..12]}";

}

public sealed record HostedRunnerPreparationResult(
    HostedRunner HostedRunner,
    IReadOnlyList<Guid> SecretGrantIds,
    bool ShouldPublishProvisionRequest,
    Guid OperationId = default);
