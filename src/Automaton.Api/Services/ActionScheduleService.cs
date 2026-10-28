using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
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

    public Task<ScheduledActionsCapability> GetCapabilityAsync(Guid userId, CancellationToken ct)
        => GetCapabilityAsync(userId, administrator: false, ct);

    public async Task<ActionSchedule> GetAsync(Guid id, Guid userId, bool administrator, CancellationToken ct)
        => await FindAuthorizedAsync(id, userId, administrator, ct);

    public async Task<ActionSchedule> CreateAsync(SaveActionScheduleRequest request, Guid userId, bool administrator, CancellationToken ct)
    {
        if (request.ApplicationId == InfrastructureAppTrust.PlatformAppId)
            throw new InvalidOperationException("Legacy System Application schedules are retired.");
        var capability = await GetCapabilityAsync(userId, administrator: false, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        if (await db.ActionSchedules.CountAsync(x => x.OwnerType == ActionScheduleOwnerTypes.User && x.OwnerUserId == userId, ct) >= capability.MaximumSchedules)
            throw new InvalidOperationException($"This plan allows at most {capability.MaximumSchedules} schedules.");

        var schedule = new ActionSchedule
        {
            Id = Guid.NewGuid(),
            OwnerType = ActionScheduleOwnerTypes.User,
            OwnerUserId = userId,
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
        if (schedule.OwnerType == ActionScheduleOwnerTypes.System
            || schedule.ApplicationId == InfrastructureAppTrust.PlatformAppId
            || request.ApplicationId == InfrastructureAppTrust.PlatformAppId)
            throw new InvalidOperationException("Legacy System Application schedules are retired.");
        var validationUserId = schedule.OwnerUserId ?? userId;
        var capability = await GetCapabilityAsync(validationUserId, administrator: false, ct);
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
            var validationUserId = schedule.OwnerUserId ?? userId;
            var capability = await GetCapabilityAsync(validationUserId, administrator: false, ct);
            if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
            await ValidateEffectiveAsync(schedule, validationUserId, administrator: false, capability, DateTime.UtcNow, ct);
            schedule.NextRunAtUtc = ActionSchedulePolicy.CalculateNextRun(schedule, DateTime.UtcNow)
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
        if (schedule.OwnerType == ActionScheduleOwnerTypes.System
            || schedule.ApplicationId == InfrastructureAppTrust.PlatformAppId)
            throw new InvalidOperationException("Legacy System Application schedules are retired.");
        var userId = schedule.OwnerUserId ?? Guid.Empty;
        var capability = await GetCapabilityAsync(userId, administrator: false, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        await ValidateEffectiveAsync(schedule, userId, administrator: false, capability, DateTime.UtcNow, ct, requireFutureRun: false);
    }

    private async Task ApplyAsync(ActionSchedule schedule, SaveActionScheduleRequest request, Guid userId, bool administrator, ScheduledActionsCapability capability, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160) throw new InvalidOperationException("Schedule name is required and cannot exceed 160 characters.");
        AppActionManifestParser.ValidateKey(request.ActionKey);
        if (request.ConcurrencyPolicy is not (ActionScheduleConcurrencyPolicies.Allow or ActionScheduleConcurrencyPolicies.SkipIfRunning or ActionScheduleConcurrencyPolicies.Queue))
            throw new InvalidOperationException("Concurrency policy is invalid.");
        ActionSchedulePolicy.ValidateMisfirePolicy(request.MisfirePolicy);
        var inputJson = request.Input.ValueKind == JsonValueKind.Undefined ? "{}" : request.Input.GetRawText();

        schedule.ApplicationId = request.ApplicationId;
        schedule.ActionKey = request.ActionKey;
        schedule.Name = request.Name.Trim();
        schedule.Description = request.Description?.Trim();
        schedule.ExecutionScope = request.ExecutionScope;
        ActionSchedulePolicy.ApplyTimingFields(schedule, request);
        schedule.TimeZone = request.TimeZone;
        schedule.Enabled = request.Enabled;
        schedule.ConcurrencyPolicy = request.ConcurrencyPolicy;
        schedule.MisfirePolicy = request.MisfirePolicy;
        schedule.InputJson = inputJson;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await ValidateEffectiveAsync(schedule, userId, administrator, capability, DateTime.UtcNow, ct);
        schedule.NextRunAtUtc = schedule.Enabled ? ActionSchedulePolicy.CalculateNextRun(schedule, DateTime.UtcNow) : null;
        if (schedule.Enabled && !schedule.NextRunAtUtc.HasValue)
            throw new InvalidOperationException("The schedule has no future occurrence.");
    }

    internal static void ApplyTimingFields(ActionSchedule schedule, SaveActionScheduleRequest request)
        => ActionSchedulePolicy.ApplyTimingFields(schedule, request);

    private async Task ValidateEffectiveAsync(ActionSchedule schedule, Guid userId, bool administrator, ScheduledActionsCapability capability, DateTime nowUtc, CancellationToken ct, bool requireFutureRun = true)
    {
        if (schedule.OwnerType == ActionScheduleOwnerTypes.System
            || schedule.ApplicationId == InfrastructureAppTrust.PlatformAppId)
            throw new InvalidOperationException("Legacy System Application schedules are retired.");
        if (schedule.OwnerType != ActionScheduleOwnerTypes.User || schedule.OwnerUserId != userId)
            throw new UnauthorizedAccessException("Scheduled Actions must be owned by the current user.");
        if (schedule.ExecutionScope != ActionExecutionScopes.AppRunner)
            throw new UnauthorizedAccessException("User schedules may target only AppRunner.");
        ActionSchedulePolicy.ValidateMisfirePolicy(schedule.MisfirePolicy);

        var manifest = await targets.ResolveAsync(schedule.ApplicationId, schedule.ActionKey, userId, ct);
        if (!manifest.Spec.Schedulable) throw new InvalidOperationException("The Action manifest does not allow scheduling.");
        if (!manifest.Spec.AllowedExecutionScopes.Contains(schedule.ExecutionScope, StringComparer.Ordinal))
            throw new InvalidOperationException("The Action manifest does not allow the requested execution scope.");
        using var input = JsonDocument.Parse(schedule.InputJson);
        var schemaError = AppActionSchemaValidator.Validate(input.RootElement, manifest.Spec.Input);
        if (schemaError != null) throw new InvalidOperationException($"Action input is invalid: {schemaError}");
        ActionSchedulePolicy.ValidateSchedule(
            schedule,
            Math.Max(capability.MinimumIntervalSeconds, manifest.Spec.MinimumIntervalSeconds),
            nowUtc,
            requireFutureRun);
    }

    internal static void ValidateSchedule(ActionSchedule schedule, int minimumIntervalSeconds, DateTime nowUtc)
        => ActionSchedulePolicy.ValidateSchedule(schedule, minimumIntervalSeconds, nowUtc);

    internal static void ValidateCronCadence(string? expression, string timeZoneId, int minimumIntervalSeconds, DateTime nowUtc)
        => ActionSchedulePolicy.ValidateCronCadence(expression, timeZoneId, minimumIntervalSeconds, nowUtc);

    internal static void ValidateMisfirePolicy(string policy)
        => ActionSchedulePolicy.ValidateMisfirePolicy(policy);

    internal static DateTime? CalculateNextRun(ActionSchedule schedule, DateTime afterUtc)
        => ActionSchedulePolicy.CalculateNextRun(schedule, afterUtc);

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
