using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Cronos;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public sealed class ActionScheduleService
{
    private readonly ApplicationDbContext db;
    private readonly IActionScheduleTargetResolver targets;
    private readonly IAuditEventService auditEvents;

    public ActionScheduleService(
        ApplicationDbContext db,
        IActionScheduleTargetResolver targets,
        IAuditEventService auditEvents)
    {
        this.db = db;
        this.targets = targets;
        this.auditEvents = auditEvents;
    }

    public async Task<IReadOnlyList<ActionSchedule>> ListAsync(Guid userId, bool administrator, CancellationToken ct)
    {
        var query = db.ActionSchedules.AsNoTracking();
        if (!administrator) query = query.Where(x => x.OwnerType == ActionScheduleOwnerTypes.User && x.OwnerUserId == userId);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<ActionSchedule> GetAsync(Guid id, Guid userId, bool administrator, CancellationToken ct)
        => await FindAuthorizedAsync(id, userId, administrator, ct);

    public async Task<ActionSchedule> CreateAsync(SaveActionScheduleRequest request, Guid userId, bool administrator, CancellationToken ct)
    {
        var system = InfrastructureAppTrust.IsTrustedApp(request.ApplicationId);
        var capability = await GetCapabilityAsync(userId, administrator && system, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        if (!system && await db.ActionSchedules.CountAsync(x => x.OwnerType == ActionScheduleOwnerTypes.User && x.OwnerUserId == userId, ct) >= capability.MaximumSchedules)
            throw new InvalidOperationException($"This plan allows at most {capability.MaximumSchedules} schedules.");

        var schedule = new ActionSchedule
        {
            Id = Guid.NewGuid(),
            OwnerType = system ? ActionScheduleOwnerTypes.System : ActionScheduleOwnerTypes.User,
            OwnerUserId = system ? null : userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        await ApplyAsync(schedule, request, userId, administrator, capability, ct);
        db.ActionSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, AuditEventTypes.ActionScheduleCreated, "create", "Action schedule created", ct);
        return schedule;
    }

    public async Task<ActionSchedule> UpdateAsync(Guid id, SaveActionScheduleRequest request, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await FindAuthorizedAsync(id, userId, administrator, ct);
        var requestedSystem = InfrastructureAppTrust.IsTrustedApp(request.ApplicationId);
        if ((schedule.OwnerType == ActionScheduleOwnerTypes.System) != requestedSystem)
            throw new InvalidOperationException("A schedule cannot change between user and system ownership.");
        var validationUserId = schedule.OwnerUserId ?? userId;
        var capability = await GetCapabilityAsync(validationUserId, administrator && requestedSystem, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        await ApplyAsync(schedule, request, validationUserId, administrator, capability, ct);
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, AuditEventTypes.ActionScheduleUpdated, "update", "Action schedule updated", ct);
        return schedule;
    }

    public async Task<ActionSchedule> SetEnabledAsync(Guid id, bool enabled, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await FindAuthorizedAsync(id, userId, administrator, ct);
        if (enabled)
        {
            var system = schedule.OwnerType == ActionScheduleOwnerTypes.System;
            var validationUserId = schedule.OwnerUserId ?? userId;
            var capability = await GetCapabilityAsync(validationUserId, administrator && system, ct);
            if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
            await ValidateEffectiveAsync(schedule, validationUserId, administrator, capability, DateTime.UtcNow, ct);
            schedule.NextRunAtUtc = CalculateNextRun(schedule, DateTime.UtcNow)
                ?? throw new InvalidOperationException("The schedule has no future occurrence.");
        }
        else
        {
            schedule.NextRunAtUtc = null;
            await db.ActionScheduleOccurrences
                .Where(x => x.ScheduleId == schedule.Id
                    && (x.Status == ActionScheduleOccurrenceStatuses.Queued
                        || x.Status == ActionScheduleOccurrenceStatuses.Published))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Skipped)
                    .SetProperty(x => x.FailureSummary, "Schedule disabled before dispatch.")
                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
        }
        schedule.Enabled = enabled;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, enabled ? AuditEventTypes.ActionScheduleEnabled : AuditEventTypes.ActionScheduleDisabled, enabled ? "enable" : "disable", $"Action schedule {(enabled ? "enabled" : "disabled")}", ct);
        return schedule;
    }

    public async Task DeleteAsync(Guid id, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await FindAuthorizedAsync(id, userId, administrator, ct);
        db.ActionSchedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, AuditEventTypes.ActionScheduleDeleted, "delete", "Action schedule deleted", ct);
    }

    public async Task ValidateForDispatchAsync(ActionSchedule schedule, CancellationToken ct)
    {
        var system = schedule.OwnerType == ActionScheduleOwnerTypes.System;
        var userId = schedule.OwnerUserId ?? Guid.Empty;
        var capability = await GetCapabilityAsync(userId, administrator: system, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        await ValidateEffectiveAsync(schedule, userId, administrator: system, capability, DateTime.UtcNow, ct);
    }

    private async Task ApplyAsync(ActionSchedule schedule, SaveActionScheduleRequest request, Guid userId, bool administrator, ScheduledActionsCapability capability, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160) throw new InvalidOperationException("Schedule name is required and cannot exceed 160 characters.");
        AppActionManifestParser.ValidateKey(request.ActionKey);
        if (request.ConcurrencyPolicy is not (ActionScheduleConcurrencyPolicies.Allow or ActionScheduleConcurrencyPolicies.SkipIfRunning or ActionScheduleConcurrencyPolicies.Queue))
            throw new InvalidOperationException("Concurrency policy is invalid.");
        ValidateMisfirePolicy(request.MisfirePolicy);
        var inputJson = request.Input.ValueKind == JsonValueKind.Undefined ? "{}" : request.Input.GetRawText();

        schedule.ApplicationId = request.ApplicationId;
        schedule.ActionKey = request.ActionKey;
        schedule.Name = request.Name.Trim();
        schedule.Description = request.Description?.Trim();
        schedule.ExecutionScope = request.ExecutionScope;
        ApplyTimingFields(schedule, request);
        schedule.TimeZone = request.TimeZone;
        schedule.Enabled = request.Enabled;
        schedule.ConcurrencyPolicy = request.ConcurrencyPolicy;
        schedule.MisfirePolicy = request.MisfirePolicy;
        schedule.InputJson = inputJson;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await ValidateEffectiveAsync(schedule, userId, administrator, capability, DateTime.UtcNow, ct);
        schedule.NextRunAtUtc = schedule.Enabled ? CalculateNextRun(schedule, DateTime.UtcNow) : null;
        if (schedule.Enabled && !schedule.NextRunAtUtc.HasValue)
            throw new InvalidOperationException("The schedule has no future occurrence.");
    }

    internal static void ApplyTimingFields(ActionSchedule schedule, SaveActionScheduleRequest request)
    {
        schedule.ScheduleType = request.ScheduleType;
        schedule.RunAtUtc = request.ScheduleType == ActionScheduleTypes.OneTime ? request.RunAtUtc?.UtcDateTime : null;
        schedule.IntervalSeconds = request.ScheduleType == ActionScheduleTypes.Interval ? request.IntervalSeconds : null;
        schedule.CronExpression = request.ScheduleType == ActionScheduleTypes.Cron ? request.CronExpression?.Trim() : null;
    }

    private async Task ValidateEffectiveAsync(ActionSchedule schedule, Guid userId, bool administrator, ScheduledActionsCapability capability, DateTime nowUtc, CancellationToken ct)
    {
        var system = schedule.OwnerType == ActionScheduleOwnerTypes.System;
        if (system != InfrastructureAppTrust.IsTrustedApp(schedule.ApplicationId))
            throw new InvalidOperationException("Schedule ownership does not match its application.");
        if (system && !administrator) throw new UnauthorizedAccessException("Only administrators may manage system schedules.");
        if (!system && schedule.ExecutionScope != ActionExecutionScopes.AppRunner)
            throw new UnauthorizedAccessException("User schedules may target only AppRunner.");
        if (schedule.ExecutionScope is not (ActionExecutionScopes.AppRunner or ActionExecutionScopes.Worker or ActionExecutionScopes.InfrastructureRunner))
            throw new InvalidOperationException("Execution scope is invalid.");
        ValidateMisfirePolicy(schedule.MisfirePolicy);

        var manifest = await targets.ResolveAsync(schedule.ApplicationId, schedule.ActionKey, userId, system, ct);
        if (!manifest.Spec.Schedulable) throw new InvalidOperationException("The Action manifest does not allow scheduling.");
        if (!manifest.Spec.AllowedExecutionScopes.Contains(schedule.ExecutionScope, StringComparer.Ordinal))
            throw new InvalidOperationException("The Action manifest does not allow the requested execution scope.");
        using var input = JsonDocument.Parse(schedule.InputJson);
        var schemaError = AppActionSchemaValidator.Validate(input.RootElement, manifest.Spec.Input);
        if (schemaError != null) throw new InvalidOperationException($"Action input is invalid: {schemaError}");
        ValidateSchedule(schedule, Math.Max(capability.MinimumIntervalSeconds, manifest.Spec.MinimumIntervalSeconds), nowUtc);
    }

    internal static void ValidateSchedule(ActionSchedule schedule, int minimumIntervalSeconds, DateTime nowUtc)
    {
        _ = GetTimeZone(schedule.TimeZone);
        if (schedule.ScheduleType == ActionScheduleTypes.OneTime)
        {
            if (!schedule.RunAtUtc.HasValue || schedule.RunAtUtc <= nowUtc) throw new InvalidOperationException("One-time run must be in the future.");
        }
        else if (schedule.ScheduleType == ActionScheduleTypes.Interval)
        {
            if (!schedule.IntervalSeconds.HasValue || schedule.IntervalSeconds < minimumIntervalSeconds)
                throw new InvalidOperationException($"Interval must be at least {minimumIntervalSeconds} seconds.");
        }
        else if (schedule.ScheduleType == ActionScheduleTypes.Cron)
        {
            ValidateCronCadence(schedule.CronExpression, schedule.TimeZone, minimumIntervalSeconds, nowUtc);
        }
        else throw new InvalidOperationException("Schedule type is invalid.");
    }

    internal static void ValidateCronCadence(string? expression, string timeZoneId, int minimumIntervalSeconds, DateTime nowUtc)
    {
        var cron = GetCron(expression);
        var timeZone = GetTimeZone(timeZoneId);
        var cursor = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var previous = cron.GetNextOccurrence(cursor, timeZone, inclusive: false)
            ?? throw new InvalidOperationException("The cron expression has no future occurrence.");
        var horizon = cursor.AddDays(366);
        const int maximumOccurrences = 100_000;
        for (var count = 1; count < maximumOccurrences && previous <= horizon; count++)
        {
            var next = cron.GetNextOccurrence(previous, timeZone, inclusive: false);
            if (!next.HasValue) return;
            if (next.Value - previous < TimeSpan.FromSeconds(minimumIntervalSeconds))
                throw new InvalidOperationException($"Cron occurrences must be at least {minimumIntervalSeconds} seconds apart.");
            previous = next.Value;
        }
    }

    internal static void ValidateMisfirePolicy(string policy)
    {
        if (policy == ActionScheduleMisfirePolicies.CatchUp)
            throw new InvalidOperationException("CatchUp schedules are not supported until bounded catch-up is implemented.");
        if (policy is not (ActionScheduleMisfirePolicies.Skip or ActionScheduleMisfirePolicies.RunOnceNow))
            throw new InvalidOperationException("Misfire policy is invalid.");
    }

    internal static DateTime? CalculateNextRun(ActionSchedule schedule, DateTime afterUtc)
        => schedule.ScheduleType switch
        {
            ActionScheduleTypes.OneTime => schedule.RunAtUtc > afterUtc ? schedule.RunAtUtc : null,
            ActionScheduleTypes.Interval => afterUtc.AddSeconds(schedule.IntervalSeconds!.Value),
            ActionScheduleTypes.Cron => GetCron(schedule.CronExpression).GetNextOccurrence(
                DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), GetTimeZone(schedule.TimeZone), inclusive: false),
            _ => null
        };

    private static CronExpression GetCron(string? expression)
    {
        try { return CronExpression.Parse(expression ?? string.Empty, CronFormat.Standard); }
        catch (CronFormatException ex) { throw new InvalidOperationException("Cron expression must use five fields: minute hour day-of-month month day-of-week.", ex); }
    }

    private static TimeZoneInfo GetTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException ex) { throw new InvalidOperationException("Time zone must be a supported IANA identifier.", ex); }
        catch (InvalidTimeZoneException ex) { throw new InvalidOperationException("Time zone is invalid.", ex); }
        catch (ArgumentException ex) { throw new InvalidOperationException("Time zone must be a supported IANA identifier.", ex); }
    }

    private async Task<ScheduledActionsCapability> GetCapabilityAsync(Guid userId, bool administrator, CancellationToken ct)
    {
        if (administrator) return new ScheduledActionsCapability { Enabled = true, MaximumSchedules = int.MaxValue, MinimumIntervalSeconds = 1, MaximumConcurrentExecutions = int.MaxValue, MonthlyExecutionLimit = int.MaxValue };
        var json = await db.Users.Where(x => x.Id == userId).Select(x => x.SubscriptionPlan != null ? x.SubscriptionPlan.CapabilitiesJson : null).SingleAsync(ct);
        if (json == null) json = await db.SubscriptionPlans.Where(x => x.Id == SubscriptionPlanIds.Free).Select(x => x.CapabilitiesJson).SingleOrDefaultAsync(ct);
        return PlanCapabilitiesSerializer.Deserialize(json).ScheduledActions;
    }

    private async Task<ActionSchedule> FindAuthorizedAsync(Guid id, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await db.ActionSchedules.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Schedule not found.");
        if (!administrator && (schedule.OwnerType != ActionScheduleOwnerTypes.User || schedule.OwnerUserId != userId))
            throw new UnauthorizedAccessException("You cannot manage this schedule.");
        return schedule;
    }

    private Task AuditAsync(ActionSchedule schedule, string eventType, string action, string message, CancellationToken ct)
        => auditEvents.WriteAsync(new AuditEventWriteRequest
        {
            EventType = eventType,
            Action = action,
            EntityType = AuditEntityTypes.ActionSchedule,
            EntityId = schedule.Id,
            Message = $"{message}: {schedule.Name}",
            Metadata = new { schedule.ApplicationId, schedule.ActionKey, schedule.ExecutionScope, schedule.ScheduleType, schedule.Enabled }
        }, ct);
}
