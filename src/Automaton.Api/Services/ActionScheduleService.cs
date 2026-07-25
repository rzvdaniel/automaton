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
    private readonly ResourceAuthorizationService authorization;
    private readonly AppFileStorageService storage;
    private readonly IAuditEventService auditEvents;

    public ActionScheduleService(
        ApplicationDbContext db,
        ResourceAuthorizationService authorization,
        AppFileStorageService storage,
        IAuditEventService auditEvents)
    {
        this.db = db;
        this.authorization = authorization;
        this.storage = storage;
        this.auditEvents = auditEvents;
    }

    public async Task<IReadOnlyList<ActionSchedule>> ListAsync(Guid userId, bool administrator, CancellationToken ct)
    {
        var query = db.ActionSchedules.AsNoTracking();
        if (!administrator) query = query.Where(x => x.OwnerUserId == userId);
        return await query.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<ActionSchedule> GetAsync(Guid id, Guid userId, bool administrator, CancellationToken ct)
        => await FindAuthorizedAsync(id, userId, administrator, ct);

    public async Task<ActionSchedule> CreateAsync(SaveActionScheduleRequest request, Guid userId, bool administrator, CancellationToken ct)
    {
        var capability = await GetCapabilityAsync(userId, administrator, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        if (!administrator && await db.ActionSchedules.CountAsync(x => x.OwnerUserId == userId, ct) >= capability.MaximumSchedules)
            throw new InvalidOperationException($"This plan allows at most {capability.MaximumSchedules} schedules.");

        var schedule = new ActionSchedule { Id = Guid.NewGuid(), OwnerUserId = userId, CreatedAtUtc = DateTime.UtcNow };
        await ApplyAsync(schedule, request, userId, administrator, capability, ct);
        db.ActionSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, AuditEventTypes.ActionScheduleCreated, "create", "Action schedule created", ct);
        return schedule;
    }

    public async Task<ActionSchedule> UpdateAsync(Guid id, SaveActionScheduleRequest request, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await FindAuthorizedAsync(id, userId, administrator, ct);
        var capability = await GetCapabilityAsync(userId, administrator, ct);
        if (!capability.Enabled) throw new InvalidOperationException("Scheduled Actions are not enabled for this plan.");
        await ApplyAsync(schedule, request, userId, administrator, capability, ct);
        await db.SaveChangesAsync(ct);
        await AuditAsync(schedule, AuditEventTypes.ActionScheduleUpdated, "update", "Action schedule updated", ct);
        return schedule;
    }

    public async Task<ActionSchedule> SetEnabledAsync(Guid id, bool enabled, Guid userId, bool administrator, CancellationToken ct)
    {
        var schedule = await FindAuthorizedAsync(id, userId, administrator, ct);
        schedule.Enabled = enabled;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        schedule.NextRunAtUtc = enabled ? CalculateNextRun(schedule, DateTime.UtcNow) : null;
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

    private async Task ApplyAsync(ActionSchedule schedule, SaveActionScheduleRequest request, Guid userId, bool administrator, ScheduledActionsCapability capability, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 160) throw new InvalidOperationException("Schedule name is required and cannot exceed 160 characters.");
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var system = InfrastructureAppTrust.IsTrustedApp(request.ApplicationId);
        if (system && !administrator) throw new UnauthorizedAccessException("Only administrators may manage system schedules.");
        if (!system && !await authorization.CanExecuteAppAsync(request.ApplicationId, userId, ct))
            throw new UnauthorizedAccessException("You do not have permission to operate this App.");
        if (!administrator && request.ExecutionScope != ActionExecutionScopes.AppRunner)
            throw new UnauthorizedAccessException("User schedules may target only AppRunner.");
        if (request.ExecutionScope is not (ActionExecutionScopes.AppRunner or ActionExecutionScopes.Worker or ActionExecutionScopes.InfrastructureRunner))
            throw new InvalidOperationException("Execution scope is invalid.");

        var manifest = await LoadManifestAsync(request.ApplicationId, request.ActionKey, system, ct);
        if (!manifest.Spec.Schedulable) throw new InvalidOperationException("The Action manifest does not allow scheduling.");
        if (!manifest.Spec.AllowedExecutionScopes.Contains(request.ExecutionScope, StringComparer.Ordinal))
            throw new InvalidOperationException("The Action manifest does not allow the requested execution scope.");
        if (request.ConcurrencyPolicy is not (ActionScheduleConcurrencyPolicies.Allow or ActionScheduleConcurrencyPolicies.SkipIfRunning or ActionScheduleConcurrencyPolicies.Queue))
            throw new InvalidOperationException("Concurrency policy is invalid.");
        if (request.MisfirePolicy is not (ActionScheduleMisfirePolicies.Skip or ActionScheduleMisfirePolicies.RunOnceNow or ActionScheduleMisfirePolicies.CatchUp))
            throw new InvalidOperationException("Misfire policy is invalid.");

        var inputJson = request.Input.ValueKind == JsonValueKind.Undefined ? "{}" : request.Input.GetRawText();
        using var input = JsonDocument.Parse(inputJson);
        var schemaError = AppActionSchemaValidator.Validate(input.RootElement, manifest.Spec.Input);
        if (schemaError != null) throw new InvalidOperationException($"Action input is invalid: {schemaError}");

        schedule.ApplicationId = request.ApplicationId;
        schedule.ActionKey = request.ActionKey;
        schedule.Name = request.Name.Trim();
        schedule.Description = request.Description?.Trim();
        schedule.ExecutionScope = request.ExecutionScope;
        schedule.ScheduleType = request.ScheduleType;
        schedule.RunAtUtc = request.RunAtUtc?.ToUniversalTime();
        schedule.IntervalSeconds = request.IntervalSeconds;
        schedule.CronExpression = request.CronExpression?.Trim();
        schedule.TimeZone = request.TimeZone;
        schedule.Enabled = request.Enabled;
        schedule.ConcurrencyPolicy = request.ConcurrencyPolicy;
        schedule.MisfirePolicy = request.MisfirePolicy;
        schedule.InputJson = inputJson;
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        var minimumInterval = Math.Max(capability.MinimumIntervalSeconds, manifest.Spec.MinimumIntervalSeconds);
        ValidateSchedule(schedule, minimumInterval, DateTime.UtcNow);
        schedule.NextRunAtUtc = schedule.Enabled ? CalculateNextRun(schedule, DateTime.UtcNow) : null;
    }

    private async Task<AppActionManifest> LoadManifestAsync(Guid appId, string actionKey, bool system, CancellationToken ct)
    {
        string root;
        if (system)
        {
            root = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure");
        }
        else
        {
            var ownerId = await db.WebAppUsers.Where(x => x.WebAppId == appId).Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(ct)
                ?? throw new KeyNotFoundException("App not found.");
            root = storage.GetAppSourceDirectoryPath(ownerId, appId);
        }
        return AppActionManifestParser.Discover(root, allowPrivilegedCapabilities: system)
            .SingleOrDefault(x => x.Key == actionKey)?.Manifest
            ?? throw new KeyNotFoundException("Action not found in the App.");
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
            _ = GetCron(schedule.CronExpression);
        }
        else throw new InvalidOperationException("Schedule type is invalid.");
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
        if (!administrator && schedule.OwnerUserId != userId) throw new UnauthorizedAccessException("You cannot manage this schedule.");
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
