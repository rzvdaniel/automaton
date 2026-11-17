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
        else if (schedule.IntervalSeconds == intervalSeconds && schedule.OwnerUserId == ownerId && schedule.ApplicationId == appId)
        {
            return;
        }
        schedule.OwnerUserId = ownerId;
        schedule.ApplicationId = appId;
        schedule.IntervalSeconds = intervalSeconds;
        schedule.UpdatedAtUtc = now;
        schedule.NextRunAtUtc = schedule.Enabled ? now + HomeCardSchedules.Stagger(appId, intervalSeconds) : null;
        await db.SaveChangesAsync(token);
    }
}
