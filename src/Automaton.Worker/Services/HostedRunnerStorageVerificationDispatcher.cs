using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Worker.Services;

internal sealed class HostedRunnerStorageVerificationDispatcher(
    IEventPublisher eventPublisher,
    HostedRunnerStorageLocalityAdoptionService storageLocalityAdoption)
{
    internal async Task DispatchPendingAsync(
        ApplicationDbContext db,
        StorageTelemetryOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var maxPerHost = Math.Clamp(options.MaxVerificationDispatchesPerHost, 1, 20);
        var candidates = await GetDispatchCandidatesAsync(db, maxPerHost, cancellationToken);
        foreach (var verification in candidates)
            await DispatchVerificationAsync(db, verification, maxPerHost, now, cancellationToken);
    }

    private static async Task<List<HostedRunnerStorageVerification>> GetDispatchCandidatesAsync(
        ApplicationDbContext db,
        int maxPerHost,
        CancellationToken cancellationToken)
    {
        var pending = await db.HostedRunnerStorageVerifications
            .Include(x => x.HostedRunner).ThenInclude(x => x.RunnerHost)
            .Where(x => x.Status == StorageVerificationStatuses.Pending
                && x.HostedRunner.RunnerHostId != null && x.HostedRunner.DeletedAt == null
                && x.HostedRunner.Status == HostedRunnerStatuses.Running && x.HostedRunner.ContainerId != null)
            .ToListAsync(cancellationToken);
        return pending.GroupBy(x => x.HostedRunner.RunnerHostId!.Value)
            .OrderBy(group => group.Min(VerificationPriority))
            .ThenBy(group => group.Min(x => x.RequestedAtUtc))
            .SelectMany(group => group.OrderBy(VerificationPriority).ThenBy(x => x.RequestedAtUtc).Take(maxPerHost))
            .Take(50).ToList();
    }

    private async Task DispatchVerificationAsync(
        ApplicationDbContext db,
        HostedRunnerStorageVerification verification,
        int maxPerHost,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var runner = verification.HostedRunner;
        var hostId = runner.RunnerHostId!.Value;
        var infrastructureRunnerId = runner.RunnerHost?.InfrastructureRunnerId;
        if (!infrastructureRunnerId.HasValue) return;
        if (runner.StorageProjectId is not > 0)
        {
            verification.Status = StorageVerificationStatuses.Failed;
            verification.CompletedAtUtc = now;
            verification.Error = "Hosted Runner storage quota identity is missing; per-user reprovisioning is no longer available for this legacy record.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        AccountStorageAllocation storageAllocation;
        try
        {
            storageAllocation = await storageLocalityAdoption.AdoptRunnerHostAsync(
                runner.UserId, hostId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            verification.Status = StorageVerificationStatuses.Failed;
            verification.CompletedAtUtc = now;
            verification.Error = exception.Message;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        if (!await TryClaimDispatchAsync(db, hostId, verification.Id, maxPerHost, now, cancellationToken)) return;
        await EnsureVerificationDeploymentAsync(db, verification, cancellationToken);
        await PublishVerificationCommandAsync(
            verification, hostId, infrastructureRunnerId.Value, storageAllocation.StorageFilesystemUuid, cancellationToken);
    }

    private static async Task EnsureVerificationDeploymentAsync(
        ApplicationDbContext db,
        HostedRunnerStorageVerification verification,
        CancellationToken cancellationToken)
    {
        var deployment = await db.HostedRunnerDeployments.FindAsync([verification.Id], cancellationToken);
        if (deployment != null) return;
        db.HostedRunnerDeployments.Add(new HostedRunnerDeployment
        {
            Id = verification.Id, HostedRunnerId = verification.HostedRunner.Id,
            Action = HostedRunnerDeploymentActions.VerifyStorage, Status = HostedRunnerDeploymentStatuses.Pending,
            StartedAt = verification.RequestedAtUtc, Logs = "Trusted host-side storage verification queued."
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task PublishVerificationCommandAsync(
        HostedRunnerStorageVerification verification,
        Guid hostId,
        Guid infrastructureRunnerId,
        string? storageFilesystemUuid,
        CancellationToken cancellationToken)
    {
        var runner = verification.HostedRunner;
        var input = new Dictionary<string, object?>
        {
            ["operationId"] = verification.Id,
            ["hostedRunnerId"] = runner.Id,
            ["runnerHostId"] = hostId,
            ["xfsProjectId"] = runner.StorageProjectId,
            ["storageAllowanceBytes"] = runner.StorageAllowanceBytes,
            ["expectedSystemVolumeName"] = $"automaton-hosted-runner-{runner.Id:N}-system",
            ["expectedUserVolumeName"] = $"automaton-hosted-runner-{runner.Id:N}-user"
        };
        if (storageFilesystemUuid != null)
            input["storageFilesystemUuid"] = storageFilesystemUuid;

        return eventPublisher.PublishAsync(new InfrastructureCommandRequestedEvent(
            CommandId: verification.Id,
            OperationId: verification.Id,
            RunnerHostId: hostId,
            InfrastructureRunnerId: infrastructureRunnerId,
            ActionKey: StorageTelemetryActions.MeasureHostedRunnerStorage,
            InputJson: JsonSerializer.Serialize(input),
            SecretGrantIds: [],
            CreatedAtUtc: DateTime.UtcNow), cancellationToken);
    }

    private static async Task<bool> TryClaimDispatchAsync(
        ApplicationDbContext db,
        Guid hostId,
        Guid verificationId,
        int maxPerHost,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            var inFlight = await CountInFlightForHostAsync(db, hostId, cancellationToken);
            if (inFlight >= maxPerHost) return false;
            return await ClaimInMemoryAsync(db, verificationId, now, cancellationToken) == 1;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM RunnerHosts WHERE Id = {hostId} FOR UPDATE", cancellationToken);
        if (await CountInFlightForHostAsync(db, hostId, cancellationToken) >= maxPerHost)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        var claimed = await db.HostedRunnerStorageVerifications
            .Where(x => x.Id == verificationId && x.Status == StorageVerificationStatuses.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, StorageVerificationStatuses.Dispatching)
                .SetProperty(x => x.StartedAtUtc, now), cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static Task<int> CountInFlightForHostAsync(ApplicationDbContext db, Guid hostId, CancellationToken cancellationToken)
        => db.HostedRunnerStorageVerifications.CountAsync(x => x.HostedRunner.RunnerHostId == hostId
            && (x.Status == StorageVerificationStatuses.Dispatching || x.Status == StorageVerificationStatuses.Running), cancellationToken);

    private static async Task<int> ClaimInMemoryAsync(
        ApplicationDbContext db,
        Guid verificationId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var item = await db.HostedRunnerStorageVerifications.SingleOrDefaultAsync(
            x => x.Id == verificationId && x.Status == StorageVerificationStatuses.Pending, cancellationToken);
        if (item == null) return 0;
        item.Status = StorageVerificationStatuses.Dispatching;
        item.StartedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    internal static int VerificationPriority(HostedRunnerStorageVerification verification)
    {
        var runner = verification.HostedRunner;
        if (runner.StorageState == HostedRunnerStorageStates.OverLimit) return 0;
        if (runner.StorageState == HostedRunnerStorageStates.DeploymentBlocked) return 1;
        if (runner.StorageState == HostedRunnerStorageStates.Warning) return 2;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Failed
            || runner.StorageSelfReportedStatus == StorageMeasurementStatuses.Failed
            || runner.StorageState == HostedRunnerStorageStates.MeasurementFailed) return 3;
        if (verification.Reason == "AdministratorRequested") return 4;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Stale
            || runner.StorageState == HostedRunnerStorageStates.Stale) return 5;
        if (verification.Reason is "Offline" or "Disconnected") return 6;
        return verification.Reason == "LowFrequencyAudit" ? 7 : 5;
    }
}
