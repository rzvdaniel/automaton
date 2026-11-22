using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Home;

/// <summary>
/// The platform's schedule for an app's <c>home-card</c> Action. It belongs to the app's owner but is not one of their
/// plan's schedules: it does not count against their schedule, run or monthly limits and the owner cannot change it.
/// </summary>
public static class HomeCardSchedules
{
    public const string IdentityPrefix = HomeCardScheduleIdentity.Prefix;

    public static bool IsPlatformSchedule(ActionSchedule schedule) => HomeCardScheduleIdentity.IsPlatformSchedule(schedule);

    public static string Identity(Guid appId) => HomeCardScheduleIdentity.For(appId);

    /// <summary>Where each app's runs fall inside its interval, so apps do not all run at the same minute.</summary>
    public static TimeSpan Stagger(Guid appId, int intervalSeconds)
        => TimeSpan.FromSeconds(BitConverter.ToUInt32(appId.ToByteArray(), 0) % (uint)intervalSeconds);

    public static int IntervalFor(ApplicationContract contract)
        => Math.Clamp(contract.HomeCard?.IntervalSeconds ?? ApplicationHomeCardSpec.DefaultIntervalSeconds,
            ApplicationHomeCardSpec.MinimumIntervalSeconds, ApplicationHomeCardSpec.MaximumIntervalSeconds);
}

/// <summary>Keeps the platform schedule of an app's <c>home-card</c> Action in step with what was deployed.</summary>
public sealed class HomeCardScheduleService(ApplicationDbContext db)
{
    /// <summary>Creates, updates or removes the schedule after a deployment. A paused app gets it switched off until resumed.</summary>
    public async Task SyncAsync(Guid appId, Guid ownerId, bool hasHomeCardAction, int intervalSeconds, CancellationToken token)
    {
        var identity = HomeCardSchedules.Identity(appId);
        var schedule = await db.ActionSchedules.SingleOrDefaultAsync(s => s.SystemIdentity == identity, token);
        if (!hasHomeCardAction)
        {
            if (schedule is null) return;
            db.ActionSchedules.Remove(schedule);
            await db.SaveChangesAsync(token);
            return;
        }

        var now = DateTime.UtcNow;
        var paused = await db.WebApps.AnyAsync(app => app.Id == appId && app.IsPaused, token);
        if (schedule is null)
        {
            schedule = new ActionSchedule
            {
                Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.User, SystemIdentity = identity,
                ActionKey = AppHomeCardLimits.ActionKey, Name = "Home card",
                Description = "Refreshes this app's card on Studio Home.", ExecutionScope = ActionExecutionScopes.AppRunner,
                ScheduleType = ActionScheduleTypes.Interval, TimeZone = "Etc/UTC",
                ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning,
                MisfirePolicy = ActionScheduleMisfirePolicies.Skip, InputJson = "{}", CreatedAtUtc = now,
                Enabled = !paused, PausedByApp = paused
            };
            db.ActionSchedules.Add(schedule);
        }
        else if (!paused)
        {
            // Repairs a schedule a pause left off.
            schedule.Enabled = true;
            schedule.PausedByApp = false;
        }
        schedule.OwnerUserId = ownerId;
        schedule.ApplicationId = appId;
        schedule.IntervalSeconds = intervalSeconds;
        schedule.UpdatedAtUtc = now;
        // A deployment just made the new version live, so the card runs now instead of waiting for the interval.
        schedule.NextRunAtUtc = schedule.Enabled ? now : null;
        await db.SaveChangesAsync(token);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> LastManualRefresh = new();

    /// <summary>The shortest time between two manual refreshes of one app's card.</summary>
    public static readonly TimeSpan ManualRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Runs the app's <c>home-card</c> Action once, now, on top of its normal interval. Returns how long to wait when the
    /// card was refreshed by hand less than a minute ago, and <c>null</c> otherwise. <c>Started</c> is false
    /// when the app has no running card schedule.
    /// </summary>
    public async Task<(bool Started, TimeSpan? RetryAfter)> RunNowAsync(Guid appId, CancellationToken token)
    {
        var identity = HomeCardSchedules.Identity(appId);
        var schedule = await db.ActionSchedules.SingleOrDefaultAsync(s => s.SystemIdentity == identity, token);
        if (schedule is null || !schedule.Enabled) return (false, null);
        var now = DateTime.UtcNow;
        if (LastManualRefresh.TryGetValue(appId, out var last) && now - last < ManualRefreshInterval)
            return (true, ManualRefreshInterval - (now - last));
        foreach (var old in LastManualRefresh.Where(item => now - item.Value >= ManualRefreshInterval).Select(item => item.Key))
            LastManualRefresh.TryRemove(old, out _);
        LastManualRefresh[appId] = now;
        if (schedule.NextRunAtUtc is null || schedule.NextRunAtUtc > now)
        {
            schedule.NextRunAtUtc = now;
            schedule.UpdatedAtUtc = now;
            await db.SaveChangesAsync(token);
        }
        return (true, null);
    }
}
