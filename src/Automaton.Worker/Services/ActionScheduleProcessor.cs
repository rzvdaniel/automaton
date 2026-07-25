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
    ITrustedSystemActionScheduleDispatchValidator trustedSchedules,
    IEnumerable<IWorkerScheduledActionHandler> workerHandlers,
    ILogger<ActionScheduleProcessor> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MisfireTolerance = TimeSpan.FromSeconds(45);
    private readonly string leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await DispatchWorkerOccurrencesAsync(now, cancellationToken);
        await DispatchQueuedAsync(now, cancellationToken);
        var ids = await db.ActionSchedules.AsNoTracking()
            .Where(x => x.Enabled && x.NextRunAtUtc <= now
                && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc < now))
            .OrderBy(x => x.NextRunAtUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            if (await TryClaimAsync(id, now, cancellationToken))
                await ProcessClaimAsync(id, now, cancellationToken);
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
        await transactions.ExecuteAsync(async ct =>
        {
            var schedule = await db.ActionSchedules.SingleAsync(x => x.Id == scheduleId, ct);
            if (!schedule.Enabled || schedule.LeaseOwner != leaseOwner || schedule.NextRunAtUtc > nowUtc)
                return;
            var capability = await GetCurrentCapabilityAsync(schedule, ct);
            if (capability == null)
            {
                schedule.Enabled = false;
                schedule.NextRunAtUtc = null;
                Release(schedule);
                await db.SaveChangesAsync(ct);
                return;
            }

            var scheduledFor = schedule.NextRunAtUtc!.Value;
            var idempotencyKey = CreateIdempotencyKey(schedule.Id, scheduledFor);
            var active = await HasActiveExecutionAsync(schedule.Id, ct);
            var ownerAtCapacity = !IsSystem(schedule)
                && await CountActiveExecutionsAsync(schedule.OwnerUserId!.Value, ct) >= capability.MaximumConcurrentExecutions;
            var monthlyLimitReached = !IsSystem(schedule)
                && await CountMonthlyOccurrencesAsync(schedule.OwnerUserId!.Value, nowUtc, ct) >= capability.MonthlyExecutionLimit;
            var status = monthlyLimitReached
                ? ActionScheduleOccurrenceStatuses.Skipped
                : ownerAtCapacity
                    ? ActionScheduleOccurrenceStatuses.Queued
                    : DetermineOccurrenceStatus(schedule, active, scheduledFor < nowUtc.Subtract(MisfireTolerance));

            var occurrence = new ActionScheduleOccurrence
            {
                Id = Guid.NewGuid(),
                ScheduleId = schedule.Id,
                ScheduledForUtc = scheduledFor,
                TriggeredAtUtc = nowUtc,
                IdempotencyKey = idempotencyKey,
                Status = status,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            db.ActionScheduleOccurrences.Add(occurrence);
            Advance(schedule, scheduledFor, nowUtc);
            Release(schedule);
            await db.SaveChangesAsync(ct);
            if (status == ActionScheduleOccurrenceStatuses.Published
                && schedule.ExecutionScope != ActionExecutionScopes.Worker)
                await events.PublishAsync(ToEvent(occurrence), ct);
            logger.LogDebug("Processed scheduled Action occurrence {OccurrenceId} with status {Status}", occurrence.Id, status);
        }, cancellationToken);
    }

    internal async Task DispatchWorkerOccurrencesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var ids = await db.ActionScheduleOccurrences.AsNoTracking()
            .Where(x => (x.Status == ActionScheduleOccurrenceStatuses.Published
                    || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                        && x.UpdatedAtUtc < nowUtc.AddMinutes(-2))
                && x.Schedule.OwnerType == ActionScheduleOwnerTypes.System
                && x.Schedule.ExecutionScope == ActionExecutionScopes.Worker)
            .OrderBy(x => x.ScheduledForUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            var existing = await db.AppModuleExecutions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == id, cancellationToken);
            if (existing != null)
            {
                var terminalStatus = existing.Status == AppModuleExecutionStatuses.Succeeded
                    ? ActionScheduleOccurrenceStatuses.Dispatched
                    : ActionScheduleOccurrenceStatuses.Failed;
                var ambiguous = existing.Status is not (
                    AppModuleExecutionStatuses.Succeeded
                    or AppModuleExecutionStatuses.Failed
                    or AppModuleExecutionStatuses.Lost);
                if (ambiguous)
                {
                    await db.AppModuleExecutions.Where(x => x.Id == existing.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.Status, AppModuleExecutionStatuses.Lost)
                            .SetProperty(x => x.FailureSummary, "Worker stopped after accepting this scheduled execution; automatic replay was suppressed.")
                            .SetProperty(x => x.CompletedAtUtc, nowUtc), cancellationToken);
                }
                await db.ActionScheduleOccurrences.Where(x => x.Id == id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ExecutionId, existing.Id)
                        .SetProperty(x => x.Status, terminalStatus)
                        .SetProperty(x => x.FailureSummary, ambiguous
                            ? "Worker execution outcome was ambiguous; automatic replay was suppressed."
                            : existing.FailureSummary)
                        .SetProperty(x => x.UpdatedAtUtc, nowUtc), cancellationToken);
                continue;
            }
            var claimed = await db.ActionScheduleOccurrences
                .Where(x => x.Id == id
                    && (x.Status == ActionScheduleOccurrenceStatuses.Published
                        || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                            && x.UpdatedAtUtc < nowUtc.AddMinutes(-2)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatching)
                    .SetProperty(x => x.Attempt, x => x.Attempt + 1)
                    .SetProperty(x => x.UpdatedAtUtc, nowUtc), cancellationToken);
            if (claimed == 0) continue;
            db.ChangeTracker.Clear();
            var occurrence = await db.ActionScheduleOccurrences.Include(x => x.Schedule)
                .SingleAsync(x => x.Id == id, cancellationToken);
            var handler = workerHandlers.SingleOrDefault(x => x.ActionKey == occurrence.Schedule.ActionKey);
            try
            {
                var manifest = await trustedSchedules.ValidateAsync(occurrence.Schedule, cancellationToken);
                if (occurrence.Schedule.ExecutionScope != ActionExecutionScopes.Worker)
                    throw new InvalidOperationException("The scheduled Action is not a Worker Action.");
                ValidateWorkerHandler(manifest, handler);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                occurrence.Status = ActionScheduleOccurrenceStatuses.Failed;
                occurrence.FailureSummary = Bound(ex.Message, 2000);
                occurrence.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }
            var execution = new AppModuleExecution
            {
                Id = Guid.NewGuid(),
                ScheduleOccurrenceId = occurrence.Id,
                AppId = occurrence.Schedule.ApplicationId,
                ModuleType = AppModuleTypes.Action,
                ModuleKey = occurrence.Schedule.ActionKey,
                TriggerType = AppModuleTriggerKinds.Scheduled,
                Status = AppModuleExecutionStatuses.Running,
                CreatedAtUtc = nowUtc,
                StartedAtUtc = nowUtc,
                CorrelationId = occurrence.IdempotencyKey
            };
            db.AppModuleExecutions.Add(execution);
            occurrence.ExecutionId = execution.Id;
            occurrence.UpdatedAtUtc = nowUtc;
            occurrence.Schedule.LastExecutionId = execution.Id;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                using var input = JsonDocument.Parse(occurrence.Schedule.InputJson);
                await handler!.ExecuteAsync(new WorkerScheduledActionContext(
                    execution.Id,
                    occurrence.Id,
                    occurrence.IdempotencyKey,
                    input.RootElement.Clone()), cancellationToken);
                execution.Status = AppModuleExecutionStatuses.Succeeded;
                execution.CompletedAtUtc = DateTime.UtcNow;
                occurrence.Status = ActionScheduleOccurrenceStatuses.Dispatched;
            }
            catch (Exception ex)
            {
                execution.Status = AppModuleExecutionStatuses.Failed;
                execution.FailureSummary = Bound(ex.Message, 1000);
                execution.CompletedAtUtc = DateTime.UtcNow;
                occurrence.Status = ActionScheduleOccurrenceStatuses.Failed;
                occurrence.FailureSummary = Bound(ex.Message, 2000);
            }
            occurrence.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DispatchQueuedAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var queued = await db.ActionScheduleOccurrences.AsNoTracking()
            .Where(x => x.Status == ActionScheduleOccurrenceStatuses.Queued)
            .OrderBy(x => x.ScheduledForUtc).Select(x => new { x.Id, x.ScheduleId }).Take(50).ToListAsync(cancellationToken);
        foreach (var item in queued)
        {
            if (await HasActiveExecutionAsync(item.ScheduleId, cancellationToken)) continue;
            var schedule = await db.ActionSchedules.AsNoTracking().SingleAsync(x => x.Id == item.ScheduleId, cancellationToken);
            var capability = await GetCurrentCapabilityAsync(schedule, cancellationToken);
            if (capability == null) continue;
            if (!IsSystem(schedule)
                && await CountActiveExecutionsAsync(schedule.OwnerUserId!.Value, cancellationToken) >= capability.MaximumConcurrentExecutions)
                continue;
            await transactions.ExecuteAsync(async ct =>
            {
                var changed = await db.ActionScheduleOccurrences
                    .Where(x => x.Id == item.Id && x.Status == ActionScheduleOccurrenceStatuses.Queued)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Published)
                        .SetProperty(x => x.TriggeredAtUtc, nowUtc)
                        .SetProperty(x => x.UpdatedAtUtc, nowUtc), ct);
                if (changed == 0) return;
                var occurrence = await db.ActionScheduleOccurrences.AsNoTracking().SingleAsync(x => x.Id == item.Id, ct);
                var scope = await db.ActionSchedules.Where(x => x.Id == item.ScheduleId)
                    .Select(x => x.ExecutionScope).SingleAsync(ct);
                if (scope != ActionExecutionScopes.Worker)
                    await events.PublishAsync(ToEvent(occurrence), ct);
            }, cancellationToken);
        }
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

    private Task<bool> HasActiveExecutionAsync(Guid scheduleId, CancellationToken ct)
        => db.ActionScheduleOccurrences
            .Where(x => x.ScheduleId == scheduleId && x.ExecutionId != null)
            .Join(db.AppModuleExecutions, x => x.ExecutionId, x => x.Id, (occurrence, execution) => execution)
            .AnyAsync(x => x.Status == AppModuleExecutionStatuses.Created
                || x.Status == AppModuleExecutionStatuses.Dispatching
                || x.Status == AppModuleExecutionStatuses.Running, ct);

    private Task<int> CountActiveExecutionsAsync(Guid ownerUserId, CancellationToken ct)
        => db.ActionScheduleOccurrences
            .Where(x => x.Schedule.OwnerUserId == ownerUserId && x.ExecutionId != null)
            .Join(db.AppModuleExecutions, x => x.ExecutionId, x => x.Id, (occurrence, execution) => execution)
            .CountAsync(x => x.Status == AppModuleExecutionStatuses.Created
                || x.Status == AppModuleExecutionStatuses.Dispatching
                || x.Status == AppModuleExecutionStatuses.Running, ct);

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
    {
        if (handler == null || !string.Equals(handler.ActionKey, manifest.Metadata.Key, StringComparison.Ordinal))
            throw new InvalidOperationException("No matching trusted Worker handler is registered for this system Action.");
    }

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
