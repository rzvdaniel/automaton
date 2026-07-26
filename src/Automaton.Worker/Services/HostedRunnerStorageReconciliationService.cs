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
    IOptions<StorageTelemetryOptions> telemetryOptions,
    ILogger<HostedRunnerStorageReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(Random.Shared.Next(1, 6)), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(
            Math.Clamp(telemetryOptions.Value.ReconciliationIntervalMinutes, 5, 60)));
        do
        {
            try { await ReconcileAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Hosted Runner storage reconciliation failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-Math.Max(30, telemetryOptions.Value.FreshAfterMinutes));
        var offlineCutoff = now.AddMinutes(-2);
        var staleRunners = await db.HostedRunners
            .Where(x => x.DeletedAt == null && x.StorageMeasuredAtUtc != null &&
                x.StorageMeasuredAtUtc < cutoff &&
                x.StorageMeasurementStatus != StorageMeasurementStatuses.Failed)
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

        var expiredObservations = await db.HostedRunnerStorageObservations
            .Where(x => x.CreatedAtUtc < now.AddDays(-Math.Max(1, telemetryOptions.Value.ObservationRetentionDays)))
            .ToListAsync(cancellationToken);
        db.HostedRunnerStorageObservations.RemoveRange(expiredObservations);
        var lowFrequencyCutoff = now.AddHours(-Math.Max(1, telemetryOptions.Value.LowFrequencyVerificationHours));
        var candidates = await db.HostedRunners
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
            .ToListAsync(cancellationToken);

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

        var staleHosts = await db.RunnerHosts
            .Where(x => x.StorageMeasuredAtUtc != null && x.StorageMeasuredAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        foreach (var host in staleHosts)
        {
            host.StorageMeasurementStatus = StorageMeasurementStatuses.Stale;
            host.StorageState = RunnerHostStorageStates.Stale;
        }
        await db.SaveChangesAsync(cancellationToken);

        var pending = await db.HostedRunnerStorageVerifications
            .Include(x => x.HostedRunner)
            .Where(x => x.Status == StorageVerificationStatuses.Pending &&
                x.HostedRunner.RunnerHostId != null && x.HostedRunner.DeletedAt == null)
            .Take(50)
            .ToListAsync(cancellationToken);
        foreach (var verification in pending)
        {
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
            var hostId = runner.RunnerHostId!.Value;
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

    private static bool IsLowFrequencySample(Guid runnerId, decimal samplePercent)
    {
        if (samplePercent <= 0) return false;
        if (samplePercent >= 100) return true;
        var bucket = BitConverter.ToUInt16(runnerId.ToByteArray(), 0) % 10000;
        return bucket < samplePercent * 100;
    }
}
