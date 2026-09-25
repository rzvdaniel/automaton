using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Cronos;

namespace Automaton.Api.Services;

internal static class ActionSchedulePolicy
{
    internal static void ApplyTimingFields(ActionSchedule schedule, SaveActionScheduleRequest request)
    {
        schedule.ScheduleType = request.ScheduleType;
        schedule.RunAtUtc = request.ScheduleType == ActionScheduleTypes.OneTime ? request.RunAtUtc?.UtcDateTime : null;
        schedule.IntervalSeconds = request.ScheduleType == ActionScheduleTypes.Interval ? request.IntervalSeconds : null;
        schedule.CronExpression = request.ScheduleType == ActionScheduleTypes.Cron ? request.CronExpression?.Trim() : null;
    }

    internal static void ValidateSchedule(ActionSchedule schedule, int minimumIntervalSeconds, DateTime nowUtc)
    {
        _ = GetTimeZone(schedule.TimeZone);
        if (schedule.ScheduleType == ActionScheduleTypes.OneTime)
        {
            if (!schedule.RunAtUtc.HasValue || schedule.RunAtUtc <= nowUtc)
                throw new InvalidOperationException("One-time run must be in the future.");
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
        else
        {
            throw new InvalidOperationException("Schedule type is invalid.");
        }
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
    {
        return schedule.ScheduleType switch
        {
            ActionScheduleTypes.OneTime => schedule.RunAtUtc > afterUtc ? schedule.RunAtUtc : null,
            ActionScheduleTypes.Interval => afterUtc.AddSeconds(schedule.IntervalSeconds!.Value),
            ActionScheduleTypes.Cron => GetCron(schedule.CronExpression).GetNextOccurrence(
                DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc),
                GetTimeZone(schedule.TimeZone),
                inclusive: false),
            _ => null
        };
    }

    private static CronExpression GetCron(string? expression)
    {
        try
        {
            return CronExpression.Parse(expression ?? string.Empty, CronFormat.Standard);
        }
        catch (CronFormatException exception)
        {
            throw new InvalidOperationException(
                "Cron expression must use five fields: minute hour day-of-month month day-of-week.",
                exception);
        }
    }

    private static TimeZoneInfo GetTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException("Time zone must be a supported IANA identifier.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException("Time zone is invalid.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Time zone must be a supported IANA identifier.", exception);
        }
    }
}
