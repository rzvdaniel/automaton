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
    ITransactionalEventExecutor transactionalEventExecutor,
    IOptions<StorageTelemetryOptions> telemetryOptions)
{
    internal Task<int> ReconcileAsync(CancellationToken cancellationToken)
        => ReconcileAsync(cancellationToken, includeHostedRunners: true, includeHosts: true);

    internal async Task<int> EnforceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var options = telemetryOptions.Value;
        var cutoff = now.AddMinutes(-Math.Max(1, options.EnforcementMeasurementValidityMinutes));
        var runners = await db.HostedRunners
            .Include(x => x.RunnerHost)
            .Where(x => x.DeletedAt == null && x.RunnerHostId != null &&
                (x.StorageState == HostedRunnerStorageStates.OverLimit ||
                 x.EnforcementState == HostedRunnerEnforcementStates.GracePeriod || x.EnforcementState == HostedRunnerEnforcementStates.SuspensionPending ||
                 x.EnforcementState == HostedRunnerEnforcementStates.Suspended || x.EnforcementState == HostedRunnerEnforcementStates.ResumePending))
            .ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var runner in runners)
        {
            var authoritative = runner.StorageMeasurementSource == StorageMeasurementSources.InfrastructureRunnerVerified &&
                runner.StorageMeasurementStatus == StorageMeasurementStatuses.Succeeded &&
                runner.StorageMeasuredAtUtc >= cutoff && runner.StorageTotalBytes.HasValue;
            if (!authoritative) continue;

            var usagePercent = runner.StorageAllowanceBytes > 0
                ? runner.StorageTotalBytes!.Value * 100m / runner.StorageAllowanceBytes : 0m;
            if (runner.EnforcementState == HostedRunnerEnforcementStates.Suspended && usagePercent <= options.RecoveryPercent)
            {
                if (await QueueLifecycleActionAsync(db, runner, StorageEnforcementActions.ResumeHostedRunner, now, cancellationToken))
                {
                    runner.EnforcementState = HostedRunnerEnforcementStates.ResumePending;
                    runner.LastEnforcementReason = $"Authoritative usage recovered to {usagePercent:0.##}% (threshold {options.RecoveryPercent:0.##}%).";
                    runner.UpdatedAt = now;
                    changed++;
                }
                continue;
            }
            if (runner.StorageState != HostedRunnerStorageStates.OverLimit) continue;
            runner.OverLimitSinceUtc ??= now;
            if (runner.EnforcementState == HostedRunnerEnforcementStates.None || runner.EnforcementState == HostedRunnerEnforcementStates.Recovered)
            {
                runner.EnforcementState = HostedRunnerEnforcementStates.GracePeriod;
                runner.GracePeriodEndsAtUtc = now.AddHours(Math.Clamp(options.OverLimitGracePeriodHours, 1, 720));
                runner.LastEnforcementReason = $"Authoritative usage is {usagePercent:0.##}% of allowance.";
                runner.LastNotificationAtUtc ??= now;
                db.AuditEvents.Add(CreateEnforcementAudit(runner, StorageTelemetryAuditEvents.EnforcementGracePeriodStarted, runner.LastEnforcementReason, now));
                changed++;
            }
            if (runner.EnforcementState == HostedRunnerEnforcementStates.GracePeriod && runner.GracePeriodEndsAtUtc <= now &&
                await QueueLifecycleActionAsync(db, runner, StorageEnforcementActions.SuspendHostedRunner, now, cancellationToken))
            {
                runner.EnforcementState = HostedRunnerEnforcementStates.SuspensionPending;
                runner.LastEnforcementReason = "Authoritative usage remained above the allowance after the grace period.";
                db.AuditEvents.Add(CreateEnforcementAudit(runner, StorageTelemetryAuditEvents.EnforcementSuspensionRequested, runner.LastEnforcementReason, now));
                changed++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private async Task<bool> QueueLifecycleActionAsync(ApplicationDbContext db, HostedRunner runner, string actionKey,
        DateTime now, CancellationToken cancellationToken)
    {
        var active = await db.HostedRunnerDeployments.AnyAsync(x => x.HostedRunnerId == runner.Id && x.Action ==
            (actionKey == StorageEnforcementActions.SuspendHostedRunner ? HostedRunnerDeploymentActions.Suspend : HostedRunnerDeploymentActions.Resume) &&
            (x.Status == HostedRunnerDeploymentStatuses.Pending || x.Status == HostedRunnerDeploymentStatuses.Queued || x.Status == HostedRunnerDeploymentStatuses.Dispatching || x.Status == HostedRunnerDeploymentStatuses.Executing || x.Status == HostedRunnerDeploymentStatuses.Running || x.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect), cancellationToken);
        if (active) return false;
        var host = runner.RunnerHost;
        if (host == null || !host.InfrastructureRunnerId.HasValue) return false;
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id,
            Action = actionKey == StorageEnforcementActions.SuspendHostedRunner ? HostedRunnerDeploymentActions.Suspend : HostedRunnerDeploymentActions.Resume,
            Status = HostedRunnerDeploymentStatuses.Pending, StartedAt = now,
            Logs = $"Trusted infrastructure Action '{actionKey}' queued for storage enforcement."
        };
        await transactionalEventExecutor.ExecuteAsync(async token =>
        {
            db.HostedRunnerDeployments.Add(operation);
            await db.SaveChangesAsync(token);
            await eventPublisher.PublishAsync(new InfrastructureCommandRequestedEvent(
                CommandId: operation.Id,
                OperationId: operation.Id,
                RunnerHostId: host.Id,
                InfrastructureRunnerId: host.InfrastructureRunnerId.Value,
                ActionKey: actionKey,
                InputJson: JsonSerializer.Serialize(new { operationId = operation.Id, hostedRunnerId = runner.Id, expectedContainerName = runner.ContainerName }),
                SecretGrantIds: [],
                CreatedAtUtc: DateTime.UtcNow), token);
        }, cancellationToken);
        return true;
    }

    private static AuditEvent CreateEnforcementAudit(HostedRunner runner, string eventType, string message, DateTime now) => new()
    {
        Id = Guid.NewGuid(), Category = "Audit", Action = "Changed", EntityType = "HostedRunner", EntityId = runner.Id,
        TargetUserId = runner.UserId, Outcome = "Success", EventType = eventType, Message = message,
        MetadataJson = JsonSerializer.Serialize(new { runner.StorageTotalBytes, runner.StorageAllowanceBytes, runner.EnforcementState }), CreatedAtUtc = now
    };

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
                .ThenInclude(x => x.RunnerHost)
            .Where(x => x.Status == StorageVerificationStatuses.Pending &&
                x.HostedRunner.RunnerHostId != null && x.HostedRunner.DeletedAt == null &&
                x.HostedRunner.Status == HostedRunnerStatuses.Running && x.HostedRunner.ContainerId != null)
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
            var infrastructureRunnerId = verification.HostedRunner.RunnerHost?.InfrastructureRunnerId;
            if (!infrastructureRunnerId.HasValue) continue;
            if (verification.HostedRunner.StorageProjectId is not > 0)
            {
                verification.Status = StorageVerificationStatuses.Failed;
                verification.CompletedAtUtc = now;
                verification.Error = "Hosted Runner storage quota identity is not initialized; reprovision is required.";
                continue;
            }
            if (!await TryClaimDispatchAsync(db, hostId, verification.Id, maxPerHost, now, cancellationToken)) continue;
            var runner = verification.HostedRunner;
            var deployment = await db.HostedRunnerDeployments.FindAsync([verification.Id], cancellationToken);
            if (deployment == null)
            {
                deployment = new HostedRunnerDeployment
                {
                    Id = verification.Id, HostedRunnerId = runner.Id,
                    Action = HostedRunnerDeploymentActions.VerifyStorage, Status = HostedRunnerDeploymentStatuses.Pending,
                    StartedAt = verification.RequestedAtUtc,
                    Logs = "Trusted host-side storage verification queued."
                };
                db.HostedRunnerDeployments.Add(deployment);
                await db.SaveChangesAsync(cancellationToken);
            }
            await eventPublisher.PublishAsync(new InfrastructureCommandRequestedEvent(
                CommandId: verification.Id,
                OperationId: verification.Id,
                RunnerHostId: hostId,
                InfrastructureRunnerId: infrastructureRunnerId.Value,
                ActionKey: StorageTelemetryActions.MeasureHostedRunnerStorage,
                InputJson: JsonSerializer.Serialize(new
                {
                    operationId = verification.Id,
                    hostedRunnerId = runner.Id,
                    runnerHostId = hostId,
                    xfsProjectId = runner.StorageProjectId,
                    expectedSystemVolumeName = $"automaton-hosted-runner-{runner.Id:N}-system",
                    expectedUserVolumeName = $"automaton-hosted-runner-{runner.Id:N}-user"
                }),
                SecretGrantIds: [],
                CreatedAtUtc: DateTime.UtcNow), cancellationToken);
        }
        return created;
    }

    private static async Task<int> CountInFlightForHostAsync(ApplicationDbContext db, Guid hostId, CancellationToken cancellationToken)
    {
        // Verification admission is bounded by the canonical verification lifecycle.
        // HostedRunnerDeployment rows are execution mirrors and can remain pending after
        // an interrupted or historical dispatch; counting them would permanently consume
        // a slot and would double-count a verification once it is genuinely in flight.
        return await db.HostedRunnerStorageVerifications
            .CountAsync(x => x.HostedRunner.RunnerHostId == hostId &&
                (x.Status == StorageVerificationStatuses.Dispatching || x.Status == StorageVerificationStatuses.Running),
                cancellationToken);
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

    internal static int VerificationPriority(HostedRunnerStorageVerification verification)
    {
        var runner = verification.HostedRunner;
        if (runner.StorageState == HostedRunnerStorageStates.OverLimit) return 0;
        if (runner.StorageState == HostedRunnerStorageStates.DeploymentBlocked) return 1;
        if (runner.StorageState == HostedRunnerStorageStates.Warning) return 2;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Failed ||
            runner.StorageSelfReportedStatus == StorageMeasurementStatuses.Failed ||
            runner.StorageState == HostedRunnerStorageStates.MeasurementFailed) return 3;
        if (verification.Reason == "AdministratorRequested") return 4;
        if (runner.StorageMeasurementStatus == StorageMeasurementStatuses.Stale ||
            runner.StorageState == HostedRunnerStorageStates.Stale) return 5;
        if (verification.Reason is "Offline" or "Disconnected") return 6;
        return verification.Reason == "LowFrequencyAudit" ? 7 : 5;
    }
}
