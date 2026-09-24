using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Worker.Services;

internal sealed class HostedRunnerStorageEnforcementService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher eventPublisher,
    ITransactionalEventExecutor transactionalEventExecutor,
    IOptions<StorageTelemetryOptions> telemetryOptions)
{
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
            changed += await EnforceRunnerAsync(db, runner, options, now, cutoff, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private async Task<int> EnforceRunnerAsync(
        ApplicationDbContext db,
        HostedRunner runner,
        StorageTelemetryOptions options,
        DateTime now,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        if (!HasAuthoritativeMeasurement(runner, cutoff)) return 0;
        var usagePercent = runner.StorageAllowanceBytes > 0
            ? runner.StorageTotalBytes!.Value * 100m / runner.StorageAllowanceBytes
            : 0m;
        if (runner.EnforcementState == HostedRunnerEnforcementStates.Suspended && usagePercent <= options.RecoveryPercent)
            return await TryQueueResumeAsync(db, runner, usagePercent, options, now, cancellationToken) ? 1 : 0;
        if (runner.StorageState != HostedRunnerStorageStates.OverLimit) return 0;
        var changed = StartGracePeriodIfNeeded(db, runner, usagePercent, options, now);
        if (await TryQueueSuspensionAsync(db, runner, now, cancellationToken)) changed++;
        return changed;
    }

    private static bool HasAuthoritativeMeasurement(HostedRunner runner, DateTime cutoff)
        => runner.StorageMeasurementSource == StorageMeasurementSources.InfrastructureRunnerVerified
            && runner.StorageMeasurementStatus == StorageMeasurementStatuses.Succeeded
            && runner.StorageMeasuredAtUtc >= cutoff
            && runner.StorageTotalBytes.HasValue;

    private async Task<bool> TryQueueResumeAsync(
        ApplicationDbContext db,
        HostedRunner runner,
        decimal usagePercent,
        StorageTelemetryOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!await QueueLifecycleActionAsync(db, runner, StorageEnforcementActions.ResumeHostedRunner, now, cancellationToken))
            return false;

        runner.EnforcementState = HostedRunnerEnforcementStates.ResumePending;
        runner.LastEnforcementReason = $"Authoritative usage recovered to {usagePercent:0.##}% (threshold {options.RecoveryPercent:0.##}%).";
        runner.UpdatedAt = now;
        return true;
    }

    private static int StartGracePeriodIfNeeded(
        ApplicationDbContext db,
        HostedRunner runner,
        decimal usagePercent,
        StorageTelemetryOptions options,
        DateTime now)
    {
        runner.OverLimitSinceUtc ??= now;
        if (runner.EnforcementState is not (HostedRunnerEnforcementStates.None or HostedRunnerEnforcementStates.Recovered))
            return 0;

        runner.EnforcementState = HostedRunnerEnforcementStates.GracePeriod;
        runner.GracePeriodEndsAtUtc = now.AddHours(Math.Clamp(options.OverLimitGracePeriodHours, 1, 720));
        runner.LastEnforcementReason = $"Authoritative usage is {usagePercent:0.##}% of allowance.";
        runner.LastNotificationAtUtc ??= now;
        db.AuditEvents.Add(CreateEnforcementAudit(
            runner,
            StorageTelemetryAuditEvents.EnforcementGracePeriodStarted,
            runner.LastEnforcementReason,
            now));
        return 1;
    }

    private async Task<bool> TryQueueSuspensionAsync(
        ApplicationDbContext db,
        HostedRunner runner,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (runner.EnforcementState != HostedRunnerEnforcementStates.GracePeriod || runner.GracePeriodEndsAtUtc > now)
            return false;
        if (!await QueueLifecycleActionAsync(db, runner, StorageEnforcementActions.SuspendHostedRunner, now, cancellationToken))
            return false;

        runner.EnforcementState = HostedRunnerEnforcementStates.SuspensionPending;
        runner.LastEnforcementReason = "Authoritative usage remained above the allowance after the grace period.";
        db.AuditEvents.Add(CreateEnforcementAudit(
            runner,
            StorageTelemetryAuditEvents.EnforcementSuspensionRequested,
            runner.LastEnforcementReason,
            now));
        return true;
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

}
