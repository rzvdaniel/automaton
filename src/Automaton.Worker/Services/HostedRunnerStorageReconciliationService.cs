using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Services;

public sealed class HostedRunnerStorageReconciliationService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher eventPublisher,
    IOptions<StorageTelemetryOptions> telemetryOptions)
{
    internal Task<int> ReconcileAsync(CancellationToken cancellationToken)
        => ReconcileAsync(cancellationToken, includeHostedRunners: true, includeHosts: true);

    internal async Task<int> ReconcileAsync(CancellationToken cancellationToken, bool includeHostedRunners, bool includeHosts)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-Math.Max(30, telemetryOptions.Value.FreshAfterMinutes));
        var offlineCutoff = now.AddMinutes(-2);
        var staleRunners = includeHostedRunners ? await db.HostedRunners
            .Where(x => x.DeletedAt == null && x.StorageMeasuredAtUtc != null &&
                x.StorageMeasuredAtUtc < cutoff &&
                x.StorageMeasurementStatus != StorageMeasurementStatuses.Failed)
            .ToListAsync(cancellationToken) : [];
        foreach (var runner in staleRunners)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), Category = "Audit", Action = "Changed",
                EntityType = "HostedRunner", EntityId = runner.Id, TargetUserId = runner.UserId,
                Outcome = "Success", EventType = StorageTelemetryAuditEvents.HostedRunnerPolicyChanged,
                Message = $"Hosted Runner storage policy changed from '{runner.StorageState}' to '{HostedRunnerStorageStates.Stale}'.",
                MetadataJson = JsonSerializer.Serialize(new { PreviousState = runner.StorageState, NewState = HostedRunnerStorageStates.Stale }),
                CreatedAtUtc = now
            });
            runner.StorageMeasurementStatus = StorageMeasurementStatuses.Stale;
            runner.StorageState = HostedRunnerStorageStates.Stale;
        }

        var timedOut = await db.HostedRunnerStorageVerifications
            .Where(x => x.Status == StorageVerificationStatuses.Running &&
                x.StartedAtUtc < now.AddMinutes(-Math.Max(1, telemetryOptions.Value.VerificationTimeoutMinutes)))
            .ToListAsync(cancellationToken);
        foreach (var verification in timedOut)
        {
            verification.Status = StorageVerificationStatuses.TimedOut;
            verification.CompletedAtUtc = now;
            verification.Error = "Host-side storage verification timed out.";
        }

        var dispatchingTimeout = now.AddMinutes(-Math.Max(2, telemetryOptions.Value.VerificationTimeoutMinutes));
        if (db.Database.IsRelational())
        {
            await db.HostedRunnerStorageVerifications
                .Where(x => x.Status == StorageVerificationStatuses.Dispatching && x.StartedAtUtc < dispatchingTimeout)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, StorageVerificationStatuses.Pending)
                    .SetProperty(x => x.StartedAtUtc, (DateTime?)null), cancellationToken);
        }
        else
        {
            var abandoned = await db.HostedRunnerStorageVerifications
                .Where(x => x.Status == StorageVerificationStatuses.Dispatching && x.StartedAtUtc < dispatchingTimeout)
                .ToListAsync(cancellationToken);
            foreach (var item in abandoned) { item.Status = StorageVerificationStatuses.Pending; item.StartedAtUtc = null; }
            await db.SaveChangesAsync(cancellationToken);
        }

        var expiredObservations = includeHostedRunners ? await db.HostedRunnerStorageObservations
            .Where(x => x.CreatedAtUtc < now.AddDays(-Math.Max(1, telemetryOptions.Value.ObservationRetentionDays)))
            .ToListAsync(cancellationToken) : [];
        db.HostedRunnerStorageObservations.RemoveRange(expiredObservations);
        var lowFrequencyCutoff = now.AddHours(-Math.Max(1, telemetryOptions.Value.LowFrequencyVerificationHours));
        var candidates = includeHostedRunners ? await db.HostedRunners
            .Where(x => x.DeletedAt == null)
            .Select(x => new
            {
                x.Id,
                x.StorageMeasuredAtUtc,
                x.StorageVerifiedAtUtc,
                x.StorageMeasurementStatus,
                x.StorageState,
                OperationalVerificationDue =
                    (x.StorageMeasuredAtUtc == null && x.StorageSelfReportedStatus != StorageMeasurementStatuses.Succeeded) ||
                    x.StorageMeasuredAtUtc < cutoff ||
                    x.StorageMeasurementStatus == StorageMeasurementStatuses.Failed ||
                    x.StorageSelfReportedStatus == StorageMeasurementStatuses.Failed ||
                    x.StorageState == HostedRunnerStorageStates.Warning ||
                    x.StorageState == HostedRunnerStorageStates.DeploymentBlocked ||
                    x.StorageState == HostedRunnerStorageStates.OverLimit ||
                    (x.RunnerId != null && x.StorageMeasuredAtUtc < cutoff && (x.Runner == null || x.Runner.ConnectionId == null ||
                        x.Runner.LastHeartbeatAtUtc == null || x.Runner.LastHeartbeatAtUtc < offlineCutoff)),
                LowFrequencyAuditDue = telemetryOptions.Value.EnableLowFrequencyVerification &&
                    (x.StorageVerifiedAtUtc == null || x.StorageVerifiedAtUtc < lowFrequencyCutoff),
                Offline = x.RunnerId != null && (x.Runner == null || x.Runner.ConnectionId == null ||
                    x.Runner.LastHeartbeatAtUtc == null || x.Runner.LastHeartbeatAtUtc < offlineCutoff)
            })
            .Where(x => x.OperationalVerificationDue || x.LowFrequencyAuditDue)
            .ToListAsync(cancellationToken) : [];

        var created = 0;
        var verificationWindow = TimeSpan.FromMinutes(Math.Max(5, telemetryOptions.Value.VerificationWindowMinutes));
        var window = now.Ticks / verificationWindow.Ticks;
        foreach (var candidate in candidates)
        {
            if (candidate.LowFrequencyAuditDue && !candidate.OperationalVerificationDue &&
                !IsLowFrequencySample(candidate.Id, telemetryOptions.Value.LowFrequencyVerificationSamplePercent))
                continue;
            var key = $"storage-verification:{candidate.Id:N}:{window}";
            if (await db.HostedRunnerStorageVerifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
                continue;
            var reason = candidate.StorageMeasuredAtUtc == null ? "NeverMeasured"
                : candidate.StorageMeasurementStatus == StorageMeasurementStatuses.Failed ? "MeasurementFailed"
                : candidate.StorageMeasuredAtUtc < cutoff ? "Stale"
                : candidate.StorageState is HostedRunnerStorageStates.Warning or HostedRunnerStorageStates.DeploymentBlocked or HostedRunnerStorageStates.OverLimit
                    ? "Threshold"
                    : candidate.Offline ? "RunnerOffline" : "Reconciliation";
            db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
            {
                Id = Guid.NewGuid(), HostedRunnerId = candidate.Id, IdempotencyKey = key,
                Status = StorageVerificationStatuses.Pending, Reason = reason, RequestedAtUtc = now
            });
            var runner = await db.HostedRunners.FindAsync([candidate.Id], cancellationToken);
            if (runner != null) runner.StorageVerificationRequestedAtUtc = now;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), Category = "Audit",
                Action = "Created", EntityType = "HostedRunner",
                EntityId = candidate.Id, TargetUserId = runner?.UserId,
                Outcome = "Success",
                EventType = StorageTelemetryAuditEvents.VerificationRequested,
                Message = "Automated hosted Runner storage verification was requested.",
                MetadataJson = JsonSerializer.Serialize(new { Reason = reason, IdempotencyKey = key }),
                CreatedAtUtc = now
            });
            created++;
        }

        var staleHosts = includeHosts ? await db.RunnerHosts
            .Where(x => x.StorageMeasuredAtUtc != null && x.StorageMeasuredAtUtc < cutoff)
            .ToListAsync(cancellationToken) : [];
        foreach (var host in staleHosts)
        {
            host.StorageMeasurementStatus = StorageMeasurementStatuses.Stale;
            host.StorageState = RunnerHostStorageStates.Stale;
        }
        await db.SaveChangesAsync(cancellationToken);

        var pending = includeHostedRunners ? await db.HostedRunnerStorageVerifications
            .Include(x => x.HostedRunner)
            .Where(x => x.Status == StorageVerificationStatuses.Pending &&
                x.HostedRunner.RunnerHostId != null && x.HostedRunner.DeletedAt == null)
            .ToListAsync(cancellationToken) : [];
        var maxPerHost = Math.Clamp(telemetryOptions.Value.MaxVerificationDispatchesPerHost, 1, 20);
        var candidatesByHost = pending.GroupBy(x => x.HostedRunner.RunnerHostId!.Value)
            .OrderBy(group => group.Min(VerificationPriority))
            .ThenBy(group => group.Min(x => x.RequestedAtUtc))
            .SelectMany(group => group.OrderBy(VerificationPriority).ThenBy(x => x.RequestedAtUtc).Take(maxPerHost))
            .Take(50).ToList();
        foreach (var verification in candidatesByHost)
        {
            var hostId = verification.HostedRunner.RunnerHostId!.Value;
            if (!await TryClaimDispatchAsync(db, hostId, verification.Id, maxPerHost, now, cancellationToken)) continue;
            var runner = verification.HostedRunner;
            var deployment = await db.HostedRunnerDeployments.FindAsync([verification.Id], cancellationToken);
            if (deployment == null)
            {
                deployment = new HostedRunnerDeployment
                {
                    Id = verification.Id, HostedRunnerId = runner.Id,
                    Action = "VerifyStorage", Status = HostedRunnerDeploymentStatuses.Pending,
                    StartedAt = verification.RequestedAtUtc,
                    Logs = "Trusted host-side storage verification queued."
                };
                db.HostedRunnerDeployments.Add(deployment);
                await db.SaveChangesAsync(cancellationToken);
            }
            await eventPublisher.PublishAsync(new InfrastructureActionExecutionRequestedEvent
            {
                OperationId = verification.Id,
                HostedRunnerId = runner.Id,
                UserId = runner.UserId,
                RunnerHostId = hostId,
                ActionKey = StorageTelemetryActions.MeasureHostedRunnerStorage,
                InputJson = JsonSerializer.Serialize(new
                {
                    operationId = verification.Id,
                    hostedRunnerId = runner.Id,
                    runnerHostId = hostId,
                    expectedSystemVolumeName = $"automaton-hosted-runner-{runner.Id:N}-system",
                    expectedUserVolumeName = $"automaton-hosted-runner-{runner.Id:N}-user"
                }),
                CorrelationId = $"storage-verification:{verification.Id:N}"
            }, cancellationToken);
        }
        return created;
    }

    private static async Task<int> CountInFlightForHostAsync(ApplicationDbContext db, Guid hostId, CancellationToken cancellationToken)
    {
        var verificationIds = await db.HostedRunnerStorageVerifications
            .Where(x => x.HostedRunner.RunnerHostId == hostId &&
                (x.Status == StorageVerificationStatuses.Dispatching || x.Status == StorageVerificationStatuses.Running))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var deploymentIds = await db.HostedRunnerDeployments
            .Where(x => x.HostedRunner.RunnerHostId == hostId &&
                (x.Status == HostedRunnerDeploymentStatuses.Pending || x.Status == HostedRunnerDeploymentStatuses.Dispatching ||
                    x.Status == HostedRunnerDeploymentStatuses.Executing || x.Status == HostedRunnerDeploymentStatuses.Running))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        return verificationIds.Concat(deploymentIds).Distinct().Count();
    }

    private static async Task<bool> TryClaimDispatchAsync(ApplicationDbContext db, Guid hostId, Guid verificationId,
        int maxPerHost, DateTime now, CancellationToken cancellationToken)
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

    private static async Task<int> ClaimInMemoryAsync(ApplicationDbContext db, Guid verificationId, DateTime now, CancellationToken cancellationToken)
    {
        var item = await db.HostedRunnerStorageVerifications.SingleOrDefaultAsync(x =>
            x.Id == verificationId && x.Status == StorageVerificationStatuses.Pending, cancellationToken);
        if (item == null) return 0;
        item.Status = StorageVerificationStatuses.Dispatching;
        item.StartedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static bool IsLowFrequencySample(Guid runnerId, decimal samplePercent)
    {
        if (samplePercent <= 0) return false;
        if (samplePercent >= 100) return true;
        var bucket = BitConverter.ToUInt16(runnerId.ToByteArray(), 0) % 10000;
        return bucket < samplePercent * 100;
    }

    private static int VerificationPriority(HostedRunnerStorageVerification verification)
    {
        var runner = verification.HostedRunner;
        if (runner.StorageState == HostedRunnerStorageStates.OverLimit) return 0;
        if (runner.StorageState == HostedRunnerStorageStates.DeploymentBlocked) return 1;
        if (runner.StorageState == HostedRunnerStorageStates.Warning) return 2;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Failed ||
            runner.StorageSelfReportedStatus == StorageMeasurementStatuses.Failed ||
            runner.StorageState == HostedRunnerStorageStates.MeasurementFailed) return 3;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Stale ||
            runner.StorageState == HostedRunnerStorageStates.Stale) return 4;
        if (verification.Reason is "Offline" or "Disconnected") return 5;
        return verification.Reason == "LowFrequencyAudit" ? 6 : 5;
    }
}
