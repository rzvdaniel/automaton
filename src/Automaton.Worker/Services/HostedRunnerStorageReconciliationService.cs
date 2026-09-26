using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Automaton.Messaging.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Services;

public sealed class HostedRunnerStorageReconciliationService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher eventPublisher,
    ITransactionalEventExecutor transactionalEventExecutor,
    IOptions<StorageTelemetryOptions> telemetryOptions)
{
    internal Task<int> ReconcileAsync(CancellationToken cancellationToken)
        => ReconcileAsync(cancellationToken, includeHostedRunners: true, includeHosts: true);

    internal Task<int> EnforceAsync(CancellationToken cancellationToken)
        => new HostedRunnerStorageEnforcementService(scopeFactory, eventPublisher, transactionalEventExecutor, telemetryOptions).EnforceAsync(cancellationToken);

    internal async Task<int> ReconcileAsync(CancellationToken cancellationToken, bool includeHostedRunners, bool includeHosts)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-Math.Max(30, telemetryOptions.Value.FreshAfterMinutes));

        await MarkStaleRunnersAsync(db, now, cutoff, includeHostedRunners, cancellationToken);
        await RecoverTimedOutVerificationsAsync(db, now, cancellationToken);
        await RemoveExpiredObservationsAsync(db, now, includeHostedRunners, cancellationToken);
        var created = await RequestDueVerificationsAsync(db, now, cutoff, includeHostedRunners, cancellationToken);
        await MarkStaleHostsAsync(db, cutoff, includeHosts, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (includeHostedRunners)
            await new HostedRunnerStorageVerificationDispatcher(
                    eventPublisher,
                    scope.ServiceProvider.GetRequiredService<HostedRunnerStorageLocalityAdoptionService>())
                .DispatchPendingAsync(db, telemetryOptions.Value, now, cancellationToken);
        return created;
    }

    private static async Task MarkStaleRunnersAsync(
        ApplicationDbContext db,
        DateTime now,
        DateTime cutoff,
        bool includeHostedRunners,
        CancellationToken cancellationToken)
    {
        if (!includeHostedRunners) return;
        var staleRunners = await db.HostedRunners
            .Where(x => x.DeletedAt == null && x.StorageMeasuredAtUtc != null
                && x.StorageMeasuredAtUtc < cutoff
                && x.StorageMeasurementStatus != StorageMeasurementStatuses.Failed)
            .ToListAsync(cancellationToken);
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
    }

    private async Task RecoverTimedOutVerificationsAsync(
        ApplicationDbContext db,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var timeout = Math.Max(1, telemetryOptions.Value.VerificationTimeoutMinutes);
        var timedOut = await db.HostedRunnerStorageVerifications
            .Where(x => x.Status == StorageVerificationStatuses.Running
                && x.StartedAtUtc < now.AddMinutes(-timeout))
            .ToListAsync(cancellationToken);
        foreach (var verification in timedOut)
        {
            verification.Status = StorageVerificationStatuses.TimedOut;
            verification.CompletedAtUtc = now;
            verification.Error = "Host-side storage verification timed out.";
        }

        var dispatchingTimeout = now.AddMinutes(-Math.Max(2, timeout));
        if (db.Database.IsRelational())
        {
            await db.HostedRunnerStorageVerifications
                .Where(x => x.Status == StorageVerificationStatuses.Dispatching && x.StartedAtUtc < dispatchingTimeout)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, StorageVerificationStatuses.Pending)
                    .SetProperty(x => x.StartedAtUtc, (DateTime?)null), cancellationToken);
            return;
        }

        var abandoned = await db.HostedRunnerStorageVerifications
            .Where(x => x.Status == StorageVerificationStatuses.Dispatching && x.StartedAtUtc < dispatchingTimeout)
            .ToListAsync(cancellationToken);
        foreach (var item in abandoned)
        {
            item.Status = StorageVerificationStatuses.Pending;
            item.StartedAtUtc = null;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RemoveExpiredObservationsAsync(
        ApplicationDbContext db,
        DateTime now,
        bool includeHostedRunners,
        CancellationToken cancellationToken)
    {
        if (!includeHostedRunners) return;
        var retentionDays = Math.Max(1, telemetryOptions.Value.ObservationRetentionDays);
        var expired = await db.HostedRunnerStorageObservations
            .Where(x => x.CreatedAtUtc < now.AddDays(-retentionDays))
            .ToListAsync(cancellationToken);
        db.HostedRunnerStorageObservations.RemoveRange(expired);
    }

    private async Task<int> RequestDueVerificationsAsync(
        ApplicationDbContext db,
        DateTime now,
        DateTime cutoff,
        bool includeHostedRunners,
        CancellationToken cancellationToken)
    {
        if (!includeHostedRunners) return 0;
        var options = telemetryOptions.Value;
        var candidates = await FindVerificationCandidatesAsync(db, now, cutoff, options, cancellationToken);
        var windowLength = TimeSpan.FromMinutes(Math.Max(5, options.VerificationWindowMinutes));
        var window = now.Ticks / windowLength.Ticks;
        var created = 0;
        foreach (var candidate in candidates)
        {
            if (await RequestCandidateVerificationAsync(db, candidate, now, cutoff, window, options, cancellationToken))
                created++;
        }
        return created;
    }

    private async Task<List<VerificationCandidate>> FindVerificationCandidatesAsync(
        ApplicationDbContext db,
        DateTime now,
        DateTime cutoff,
        StorageTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        var offlineCutoff = now.AddMinutes(-2);
        var lowFrequencyCutoff = now.AddHours(-Math.Max(1, options.LowFrequencyVerificationHours));
        var operationalIds = await FindOperationalVerificationIdsAsync(db, cutoff, offlineCutoff, cancellationToken);
        List<Guid> lowFrequencyIds = options.EnableLowFrequencyVerification
            ? await FindLowFrequencyVerificationIdsAsync(db, lowFrequencyCutoff, cancellationToken)
            : [];
        var candidateIds = operationalIds.Union(lowFrequencyIds).ToArray();
        if (candidateIds.Length == 0) return [];

        var source = await db.HostedRunners
            .Where(x => candidateIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.StorageMeasuredAtUtc,
                x.StorageVerifiedAtUtc,
                x.StorageMeasurementStatus,
                x.StorageSelfReportedStatus,
                x.StorageState,
                Offline = x.RunnerId != null && (x.Runner == null || x.Runner.ConnectionId == null
                    || x.Runner.LastHeartbeatAtUtc == null || x.Runner.LastHeartbeatAtUtc < offlineCutoff)
            })
            .ToListAsync(cancellationToken);

        var operational = operationalIds.ToHashSet();
        var lowFrequency = lowFrequencyIds.ToHashSet();
        return source.Select(x => new VerificationCandidate(
                x.Id,
                x.StorageMeasuredAtUtc,
                x.StorageVerifiedAtUtc,
                x.StorageMeasurementStatus,
                x.StorageSelfReportedStatus,
                x.StorageState,
                operational.Contains(x.Id),
                lowFrequency.Contains(x.Id),
                x.Offline))
            .ToList();
    }

    private static async Task<Guid[]> FindOperationalVerificationIdsAsync(
        ApplicationDbContext db,
        DateTime cutoff,
        DateTime offlineCutoff,
        CancellationToken cancellationToken)
    {
        var neverMeasuredOrStale = await db.HostedRunners
            .Where(x => x.DeletedAt == null &&
                ((x.StorageMeasuredAtUtc == null && x.StorageSelfReportedStatus != StorageMeasurementStatuses.Succeeded)
                    || x.StorageMeasuredAtUtc < cutoff))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var failedMeasurements = await db.HostedRunners
            .Where(x => x.DeletedAt == null && (x.StorageMeasurementStatus == StorageMeasurementStatuses.Failed
                || x.StorageSelfReportedStatus == StorageMeasurementStatuses.Failed))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var thresholdStates = await db.HostedRunners
            .Where(x => x.DeletedAt == null && (x.StorageState == HostedRunnerStorageStates.Warning
                || x.StorageState == HostedRunnerStorageStates.DeploymentBlocked
                || x.StorageState == HostedRunnerStorageStates.OverLimit))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var offlineMeasured = await db.HostedRunners
            .Where(x => x.DeletedAt == null && x.RunnerId != null && x.StorageMeasuredAtUtc < cutoff
                && (x.Runner == null || x.Runner.ConnectionId == null || x.Runner.LastHeartbeatAtUtc == null
                    || x.Runner.LastHeartbeatAtUtc < offlineCutoff))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return neverMeasuredOrStale
            .Concat(failedMeasurements)
            .Concat(thresholdStates)
            .Concat(offlineMeasured)
            .Distinct()
            .ToArray();
    }

    private static Task<List<Guid>> FindLowFrequencyVerificationIdsAsync(
        ApplicationDbContext db,
        DateTime lowFrequencyCutoff,
        CancellationToken cancellationToken)
        => db.HostedRunners
            .Where(x => x.DeletedAt == null && (x.StorageVerifiedAtUtc == null || x.StorageVerifiedAtUtc < lowFrequencyCutoff))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

    private async Task<bool> RequestCandidateVerificationAsync(
        ApplicationDbContext db,
        VerificationCandidate candidate,
        DateTime now,
        DateTime cutoff,
        long window,
        StorageTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        if (!ShouldRequestCandidate(candidate, options)) return false;
        var key = $"storage-verification:{candidate.Id:N}:{window}";
        if (await db.HostedRunnerStorageVerifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
            return false;

        var reason = GetVerificationReason(candidate, cutoff);
        db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
        {
            Id = Guid.NewGuid(), HostedRunnerId = candidate.Id, IdempotencyKey = key,
            Status = StorageVerificationStatuses.Pending, Reason = reason, RequestedAtUtc = now
        });
        var runner = await db.HostedRunners.FindAsync([candidate.Id], cancellationToken);
        if (runner != null) runner.StorageVerificationRequestedAtUtc = now;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = "Audit", Action = "Created", EntityType = "HostedRunner",
            EntityId = candidate.Id, TargetUserId = runner?.UserId, Outcome = "Success",
            EventType = StorageTelemetryAuditEvents.VerificationRequested,
            Message = "Automated hosted Runner storage verification was requested.",
            MetadataJson = JsonSerializer.Serialize(new { Reason = reason, IdempotencyKey = key }),
            CreatedAtUtc = now
        });
        return true;
    }

    private static bool ShouldRequestCandidate(VerificationCandidate candidate, StorageTelemetryOptions options)
        => !candidate.LowFrequencyAuditDue || candidate.OperationalVerificationDue
            || IsLowFrequencySample(candidate.Id, options.LowFrequencyVerificationSamplePercent);

    private static string GetVerificationReason(VerificationCandidate candidate, DateTime cutoff)
    {
        if (candidate.StorageMeasuredAtUtc == null) return "NeverMeasured";
        if (candidate.StorageMeasurementStatus == StorageMeasurementStatuses.Failed) return "MeasurementFailed";
        if (candidate.StorageMeasuredAtUtc < cutoff) return "Stale";
        if (HasThresholdState(candidate.StorageState)) return "Threshold";
        return candidate.Offline ? "RunnerOffline" : "Reconciliation";
    }

    private static bool HasThresholdState(string storageState)
        => storageState is HostedRunnerStorageStates.Warning
            or HostedRunnerStorageStates.DeploymentBlocked
            or HostedRunnerStorageStates.OverLimit;

    private static async Task MarkStaleHostsAsync(
        ApplicationDbContext db,
        DateTime cutoff,
        bool includeHosts,
        CancellationToken cancellationToken)
    {
        if (!includeHosts) return;
        var staleHosts = await db.RunnerHosts
            .Where(x => x.StorageMeasuredAtUtc != null && x.StorageMeasuredAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        foreach (var host in staleHosts)
        {
            host.StorageMeasurementStatus = StorageMeasurementStatuses.Stale;
            host.StorageState = RunnerHostStorageStates.Stale;
        }
    }

    private sealed record VerificationCandidate(
        Guid Id,
        DateTime? StorageMeasuredAtUtc,
        DateTime? StorageVerifiedAtUtc,
        string StorageMeasurementStatus,
        string? StorageSelfReportedStatus,
        string StorageState,
        bool OperationalVerificationDue,
        bool LowFrequencyAuditDue,
        bool Offline);
    private static bool IsLowFrequencySample(Guid runnerId, decimal samplePercent)
    {
        if (samplePercent <= 0) return false;
        if (samplePercent >= 100) return true;
        var bucket = BitConverter.ToUInt16(runnerId.ToByteArray(), 0) % 10000;
        return bucket < samplePercent * 100;
    }

    internal static int VerificationPriority(HostedRunnerStorageVerification verification)
        => HostedRunnerStorageVerificationDispatcher.VerificationPriority(verification);
}
