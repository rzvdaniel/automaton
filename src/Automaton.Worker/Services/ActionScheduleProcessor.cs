using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Cronos;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Worker.Services;

public sealed class ActionScheduleProcessor(
    ApplicationDbContext db,
    IEventPublisher events,
    ITransactionalEventExecutor transactions,
    WorkerScheduledActionDispatcher workerDispatcher,
    ILogger<ActionScheduleProcessor> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MisfireTolerance = TimeSpan.FromSeconds(45);
    private readonly string leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await workerDispatcher.DispatchAsync(now, cancellationToken);
        await DispatchQueuedAsync(now, cancellationToken);
        var ids = await db.ActionSchedules.AsNoTracking()
            .Where(x => x.Enabled && x.NextRunAtUtc <= now
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .OrderBy(x => x.NextRunAtUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            if (await TryClaimAsync(id, now, cancellationToken))
            {
                logger.LogInformation(
                    "Claimed due Action schedule {ScheduleId} with lease {LeaseOwner} expiring at {LeaseExpiryUtc}",
                    id,
                    leaseOwner,
                    now.Add(LeaseDuration));
                try
                {
                    await ProcessClaimAsync(id, now, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to persist due Action schedule occurrence for {ScheduleId} with lease {LeaseOwner}; releasing lease without advancing the schedule",
                        id,
                        leaseOwner);
                    await ReleaseFailedClaimAsync(id, cancellationToken);
                }
            }
        }
    }

    internal async Task<bool> TryClaimAsync(Guid scheduleId, DateTime nowUtc, CancellationToken cancellationToken)
        => await db.ActionSchedules
            .Where(x => x.Id == scheduleId && x.Enabled && x.NextRunAtUtc <= nowUtc
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < nowUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseOwner, leaseOwner)
                .SetProperty(x => x.LeaseExpiresAtUtc, nowUtc.Add(LeaseDuration)), cancellationToken) == 1;

    private async Task ProcessClaimAsync(Guid scheduleId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        Guid? occurrenceId = null;
        string? systemIdentity = null;
        string? occurrenceStatus = null;
        string? executionScope = null;
        var eventPublished = false;
        var ownerId = await db.ActionSchedules.AsNoTracking()
            .Where(x => x.Id == scheduleId).Select(x => x.OwnerUserId).SingleAsync(cancellationToken);
        await transactions.ExecuteAsync(
            async ct =>
            {
                await LockAccountAdmissionAsync(ownerId, ct);
                await ProcessScheduleWithinTransactionAsync(
                    scheduleId, nowUtc, ct,
                    result =>
                    {
                        occurrenceId = result.OccurrenceId;
                        systemIdentity = result.SystemIdentity;
                        executionScope = result.ExecutionScope;
                        occurrenceStatus = result.OccurrenceStatus;
                        eventPublished = result.EventPublished;
                    });
            }, cancellationToken);
        if (occurrenceId.HasValue)
        {
            logger.LogInformation(
                "Committed Action schedule transaction for {ScheduleId} ({SystemIdentity}), occurrence {OccurrenceId}; dispatch publication result {DispatchPublicationResult}",
                scheduleId,
                systemIdentity,
                occurrenceId,
                eventPublished
                    ? "published"
                    : executionScope == ActionExecutionScopes.Worker
                        ? "worker-dispatch-pending"
                        : "not-published");
        }
    }

    private async Task ProcessScheduleWithinTransactionAsync(
        Guid scheduleId,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        Action<ClaimResult> setResult)
    {
        var schedule = await db.ActionSchedules.SingleAsync(x => x.Id == scheduleId, cancellationToken);
        if (!await ValidateClaimAsync(schedule, nowUtc, cancellationToken))
        {
            setResult(new ClaimResult(null, schedule.SystemIdentity, schedule.ExecutionScope, null, false));
            return;
        }

        var capability = await GetCurrentCapabilityAsync(schedule, cancellationToken);
        if (capability == null)
        {
            await DisableUnschedulableScheduleAsync(schedule, cancellationToken);
            setResult(new ClaimResult(null, schedule.SystemIdentity, schedule.ExecutionScope, null, false));
            return;
        }

        var result = await PersistOccurrenceAsync(schedule, capability, nowUtc, cancellationToken);
        setResult(result);
    }

    private async Task<bool> ValidateClaimAsync(ActionSchedule schedule, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (schedule.Enabled && schedule.LeaseOwner == leaseOwner && schedule.NextRunAtUtc <= nowUtc) return true;
        if (schedule.LeaseOwner == leaseOwner)
        {
            Release(schedule);
            await db.SaveChangesAsync(cancellationToken);
        }
        logger.LogWarning(
            "Skipped claimed Action schedule {ScheduleId} ({SystemIdentity}) because validation of the current lease failed. Expected lease {LeaseOwner} and due time at or before {NowUtc}; actual lease {ActualLeaseOwner}, expiry {LeaseExpiryUtc}, enabled {Enabled}, next run {NextRunAtUtc}",
            schedule.Id, schedule.SystemIdentity, leaseOwner, nowUtc, schedule.LeaseOwner,
            schedule.LeaseExpiresAtUtc, schedule.Enabled, schedule.NextRunAtUtc);
        return false;
    }

    private async Task DisableUnschedulableScheduleAsync(ActionSchedule schedule, CancellationToken cancellationToken)
    {
        schedule.Enabled = false;
        schedule.NextRunAtUtc = null;
        Release(schedule);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Disabled Action schedule {ScheduleId} ({SystemIdentity}) because its current validation outcome is not schedulable; lease released",
            schedule.Id, schedule.SystemIdentity);
    }

    private async Task<ClaimResult> PersistOccurrenceAsync(
        ActionSchedule schedule,
        ScheduledActionsCapability capability,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var scheduledFor = schedule.NextRunAtUtc!.Value;
        var occurrenceStatus = await DetermineClaimedOccurrenceStatusAsync(schedule, capability, scheduledFor, nowUtc, cancellationToken);
        var occurrence = new ActionScheduleOccurrence
        {
            Id = Guid.NewGuid(), ScheduleId = schedule.Id, ScheduledForUtc = scheduledFor,
            TriggeredAtUtc = nowUtc, IdempotencyKey = CreateIdempotencyKey(schedule.Id, scheduledFor),
            Status = occurrenceStatus, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc
        };
        db.ActionScheduleOccurrences.Add(occurrence);
        Advance(schedule, scheduledFor, nowUtc);
        Release(schedule);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Persisted Action schedule occurrence {OccurrenceId} for {ScheduleId} ({SystemIdentity}) with validation outcome Succeeded, status {Status}, next run {NextRunAtUtc}, and released lease {LeaseOwner}",
            occurrence.Id, schedule.Id, schedule.SystemIdentity, occurrenceStatus, schedule.NextRunAtUtc, leaseOwner);

        var published = occurrenceStatus == ActionScheduleOccurrenceStatuses.Published
            && schedule.ExecutionScope != ActionExecutionScopes.Worker;
        if (published)
        {
            await events.PublishAsync(ToEvent(occurrence), cancellationToken);
            logger.LogInformation(
                "Published dispatch event for Action schedule occurrence {OccurrenceId} on {ScheduleId} ({SystemIdentity})",
                occurrence.Id, schedule.Id, schedule.SystemIdentity);
        }
        return new ClaimResult(occurrence.Id, schedule.SystemIdentity, schedule.ExecutionScope, occurrenceStatus, published);
    }

    private async Task<string> DetermineClaimedOccurrenceStatusAsync(
        ActionSchedule schedule,
        ScheduledActionsCapability capability,
        DateTime scheduledFor,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var active = await HasActiveExecutionAsync(schedule.Id, cancellationToken);
        if (IsSystem(schedule))
            return DetermineOccurrenceStatus(schedule, active, scheduledFor < nowUtc.Subtract(MisfireTolerance));
        var ownerId = schedule.OwnerUserId!.Value;
        var atCapacity = await CountActiveExecutionsAsync(ownerId, cancellationToken) >= capability.MaximumConcurrentExecutions;
        if (await CountMonthlyOccurrencesAsync(ownerId, nowUtc, cancellationToken) >= capability.MonthlyExecutionLimit)
            return ActionScheduleOccurrenceStatuses.Skipped;
        return atCapacity ? ActionScheduleOccurrenceStatuses.Queued
            : DetermineOccurrenceStatus(schedule, active, scheduledFor < nowUtc.Subtract(MisfireTolerance));
    }

    private sealed record ClaimResult(Guid? OccurrenceId, string? SystemIdentity, string? ExecutionScope, string? OccurrenceStatus, bool EventPublished);

    private async Task ReleaseFailedClaimAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var released = await db.ActionSchedules
            .Where(x => x.Id == scheduleId && x.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
        logger.LogWarning(
            "Released failed Action schedule claim for {ScheduleId} with lease {LeaseOwner}; rows released {ReleasedRows}",
            scheduleId,
            leaseOwner,
            released);
    }

    internal async Task DispatchWorkerOccurrencesAsync(DateTime nowUtc, CancellationToken cancellationToken)
        => await workerDispatcher.DispatchAsync(nowUtc, cancellationToken);

    private async Task DispatchQueuedAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var queued = await db.ActionScheduleOccurrences.AsNoTracking()
            .Where(x => x.Status == ActionScheduleOccurrenceStatuses.Queued)
            .OrderBy(x => x.ScheduledForUtc).Select(x => new { x.Id, x.ScheduleId }).Take(50).ToListAsync(cancellationToken);
        foreach (var item in queued)
        {
            var ownerId = await db.ActionSchedules.AsNoTracking()
                .Where(x => x.Id == item.ScheduleId).Select(x => x.OwnerUserId).SingleAsync(cancellationToken);
            await transactions.ExecuteAsync(
                ct => DispatchQueuedWithinTransactionAsync(item.Id, item.ScheduleId, ownerId, nowUtc, ct),
                cancellationToken);
        }
    }

    private async Task DispatchQueuedWithinTransactionAsync(
        Guid occurrenceId, Guid scheduleId, Guid? ownerId, DateTime nowUtc, CancellationToken ct)
    {
        await LockAccountAdmissionAsync(ownerId, ct);
        if (await HasActiveExecutionAsync(scheduleId, ct)) return;
        var schedule = await db.ActionSchedules.AsNoTracking().SingleAsync(x => x.Id == scheduleId, ct);
        var capability = await GetCurrentCapabilityAsync(schedule, ct);
        if (capability == null) return;
        if (!IsSystem(schedule)
            && await CountActiveExecutionsAsync(schedule.OwnerUserId!.Value, ct) >= capability.MaximumConcurrentExecutions)
            return;
        var changed = await db.ActionScheduleOccurrences
            .Where(x => x.Id == occurrenceId && x.Status == ActionScheduleOccurrenceStatuses.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Published)
                .SetProperty(x => x.TriggeredAtUtc, nowUtc)
                .SetProperty(x => x.UpdatedAtUtc, nowUtc), ct);
        if (changed == 0) return;
        var occurrence = await db.ActionScheduleOccurrences.AsNoTracking().SingleAsync(x => x.Id == occurrenceId, ct);
        if (schedule.ExecutionScope != ActionExecutionScopes.Worker)
            await events.PublishAsync(ToEvent(occurrence), ct);
    }

    private async Task<ScheduledActionsCapability?> GetCurrentCapabilityAsync(ActionSchedule schedule, CancellationToken ct)
    {
        if (IsSystem(schedule))
            return new ScheduledActionsCapability
            {
                Enabled = true,
                MaximumSchedules = int.MaxValue,
                MinimumIntervalSeconds = 1,
                MaximumConcurrentExecutions = int.MaxValue,
                MonthlyExecutionLimit = int.MaxValue
            };
        if (!schedule.OwnerUserId.HasValue || !await db.WebApps.AnyAsync(x => x.Id == schedule.ApplicationId, ct)) return null;
        var capabilityJson = await db.Users.Where(x => x.Id == schedule.OwnerUserId.Value)
            .Select(x => x.SubscriptionPlan != null ? x.SubscriptionPlan.CapabilitiesJson : null).SingleOrDefaultAsync(ct);
        if (capabilityJson == null)
            capabilityJson = await db.SubscriptionPlans.Where(x => x.Id == SubscriptionPlanIds.Free)
                .Select(x => x.CapabilitiesJson).SingleOrDefaultAsync(ct);
        var capability = PlanCapabilitiesSerializer.Deserialize(capabilityJson).ScheduledActions;
        return capability.Enabled ? capability : null;
    }

    private Task LockAccountAdmissionAsync(Guid? ownerId, CancellationToken ct)
    {
        if (!ownerId.HasValue || db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) != true)
            return Task.CompletedTask;
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Scheduled account admission requires a database transaction.");
        // This is the first database read inside the transaction: subsequent consistent reads
        // must see the previous owner's committed reservations after waiting for this lock.
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM AspNetUsers WHERE Id = {ownerId.Value} FOR UPDATE", ct);
    }

    private Task<bool> HasActiveExecutionAsync(Guid scheduleId, CancellationToken ct)
        => ActionScheduleCapacityReservations.Active(db)
            .AnyAsync(x => x.ScheduleId == scheduleId, ct);

    private Task<int> CountActiveExecutionsAsync(Guid ownerUserId, CancellationToken ct)
        => ActionScheduleCapacityReservations.Active(db)
            .CountAsync(x => x.Schedule.OwnerUserId == ownerUserId, ct);

    private Task<int> CountMonthlyOccurrencesAsync(Guid ownerUserId, DateTime nowUtc, CancellationToken ct)
    {
        var start = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return db.ActionScheduleOccurrences.CountAsync(x => x.Schedule.OwnerUserId == ownerUserId
            && x.CreatedAtUtc >= start
            && x.Status != ActionScheduleOccurrenceStatuses.Skipped, ct);
    }

    private static bool IsSystem(ActionSchedule schedule) => schedule.OwnerType == ActionScheduleOwnerTypes.System;

    internal static void Advance(ActionSchedule schedule, DateTime scheduledForUtc, DateTime nowUtc)
    {
        schedule.LastTriggeredAtUtc = nowUtc;
        if (schedule.ScheduleType == ActionScheduleTypes.OneTime)
        {
            schedule.Enabled = false;
            schedule.NextRunAtUtc = null;
            return;
        }
        if (schedule.ScheduleType == ActionScheduleTypes.Interval)
        {
            var next = scheduledForUtc;
            do next = next.AddSeconds(schedule.IntervalSeconds!.Value); while (next <= nowUtc);
            schedule.NextRunAtUtc = next;
            return;
        }
        var cron = CronExpression.Parse(schedule.CronExpression!, CronFormat.Standard);
        schedule.NextRunAtUtc = cron.GetNextOccurrence(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone),
            inclusive: false);
        if (!schedule.NextRunAtUtc.HasValue) schedule.Enabled = false;
    }

    internal static string DetermineOccurrenceStatus(ActionSchedule schedule, bool hasActiveExecution, bool isMisfired)
    {
        if (isMisfired && schedule.MisfirePolicy == ActionScheduleMisfirePolicies.Skip)
            return ActionScheduleOccurrenceStatuses.Skipped;
        if (hasActiveExecution && schedule.ConcurrencyPolicy == ActionScheduleConcurrencyPolicies.SkipIfRunning)
            return ActionScheduleOccurrenceStatuses.Skipped;
        if (hasActiveExecution && schedule.ConcurrencyPolicy == ActionScheduleConcurrencyPolicies.Queue)
            return ActionScheduleOccurrenceStatuses.Queued;
        return ActionScheduleOccurrenceStatuses.Published;
    }

    internal static string CreateIdempotencyKey(Guid scheduleId, DateTime scheduledForUtc)
        => $"schedule:{scheduleId}:{DateTime.SpecifyKind(scheduledForUtc, DateTimeKind.Utc):O}";

    internal static bool IsQueuedDispatchEligible(ActionScheduleOccurrence occurrence)
        => occurrence.Status == ActionScheduleOccurrenceStatuses.Queued;

    internal static void ValidateWorkerHandler(
        AppActionManifest manifest,
        IWorkerScheduledActionHandler? handler)
        => WorkerScheduledActionDispatcher.ValidateWorkerHandler(manifest, handler);

    private static ScheduledActionExecutionRequestedEvent ToEvent(ActionScheduleOccurrence occurrence) => new()
    {
        OccurrenceId = occurrence.Id,
        ScheduleId = occurrence.ScheduleId,
        ScheduledForUtc = occurrence.ScheduledForUtc,
        IdempotencyKey = occurrence.IdempotencyKey
    };

    private static void Release(ActionSchedule schedule)
    {
        schedule.LeaseOwner = null;
        schedule.LeaseExpiresAtUtc = null;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Bound(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
