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
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class HostedRunnerProvisioningService
{
    private readonly ApplicationDbContext db;
    private readonly IEventPublisher eventPublisher;
    private readonly HostedRunnerOptions options;
    private readonly HostedRunnerProvisioningPolicyService provisioningPolicyService;
    private readonly IAccountStorageProjectAllocationService storageProjectAllocationService;
    private readonly ILogger<HostedRunnerProvisioningService> logger;
    private readonly HostedRunnerProvisioningPreparationService preparationService;

    public HostedRunnerProvisioningService(
        ApplicationDbContext db,
        IEventPublisher eventPublisher,
        IOptionsSnapshot<HostedRunnerOptions> options,
        HostedRunnerProvisioningPolicyService provisioningPolicyService,
        IAccountStorageProjectAllocationService storageProjectAllocationService,
        ILogger<HostedRunnerProvisioningService> logger,
        HostedRunnerProvisioningPreparationService preparationService)
    {
        this.db = db;
        this.eventPublisher = eventPublisher;
        this.options = options.Value;
        this.provisioningPolicyService = provisioningPolicyService;
        this.storageProjectAllocationService = storageProjectAllocationService;
        this.logger = logger;
        this.preparationService = preparationService;
    }

    public async Task<HostedRunnerPreparationResult> PrepareProvisioningAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken = default,
        bool forceProvision = false)
    {
        var policyResolution = await provisioningPolicyService.ResolveAsync(userId, planId, cancellationToken);
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

        return await preparationService.PrepareOnSelectedHostAsync(
            userId, hostedRunner, requestedSnapshot, accountStorageProjectId, createdHostedRunner,
            previousHostId, cancellationToken);
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
        => await preparationService.DisableHostedRunnersForUserAsync(userId, reason, cancellationToken);

    private static string BuildRunnerName(Guid userId) => $"Hosted Runner {userId.ToString("N")[..8]}";

    private static string BuildContainerName(Guid userId) => $"automaton-hosted-runner-{userId.ToString("N")[..12]}";

}

public sealed record HostedRunnerPreparationResult(
    HostedRunner HostedRunner,
    IReadOnlyList<Guid> SecretGrantIds,
    bool ShouldPublishProvisionRequest,
    Guid OperationId = default);
