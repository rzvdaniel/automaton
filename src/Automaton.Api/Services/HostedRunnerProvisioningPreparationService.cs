using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Data;

namespace Automaton.Api.Services;

/// <summary>
/// Persists the selected host, deployment, and enrollment state for a Hosted Runner provisioning operation.
/// </summary>
public sealed class HostedRunnerProvisioningPreparationService(
    ApplicationDbContext db,
    RunnerEnrollmentService runnerEnrollmentService,
    IAuditEventService auditEventService,
    IOptionsSnapshot<HostedRunnerOptions> options,
    HostedRunnerHostPlacementService hostPlacementService,
    ILogger<HostedRunnerProvisioningPreparationService> logger)
{
    private readonly HostedRunnerOptions settings = options.Value;

    public async Task<HostedRunnerPreparationResult> PrepareOnSelectedHostAsync(
        Guid userId,
        HostedRunner hostedRunner,
        HostedRunnerSpecificationSnapshot requestedSnapshot,
        int accountStorageProjectId,
        bool createdHostedRunner,
        Guid? previousHostId,
        Guid? storageRunnerHostId,
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
                storageRunnerHostId,
                cancellationToken);
            var host = hostSelection.Host;
            if (host == null)
            {
                var failedPreparation = await RecordNoHostAvailableAsync(hostedRunner, hostSelection.FailureReason, cancellationToken);
                await CommitCapacityTransactionAsync(capacityTransaction, cancellationToken);
                return failedPreparation;
            }

            hostedRunner.RunnerHostId = host.Id;
            hostedRunner.StorageAllowanceBytes = requestedSnapshot.Resources.DiskLimitMb * 1024L * 1024L;
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

            hostedRunner.Status = settings.Enabled
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
                await capacityTransaction.DisposeAsync();
        }
    }

    public async Task DisableHostedRunnersForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken)
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
                IsProvisioningEnabled = settings.Enabled
            }
        }, cancellationToken);

        return AddDeployment(
            hostedRunner,
            HostedRunnerDeploymentStatuses.Queued,
            $"Trusted infrastructure Action: {(createdHostedRunner ? "provision-hosted-runner" : "reprovision-hosted-runner")} on Runner Host '{host.Name}'.",
            settings.Enabled
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
        if (!settings.Enabled)
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

    private static Task CommitCapacityTransactionAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
        => transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);
}
