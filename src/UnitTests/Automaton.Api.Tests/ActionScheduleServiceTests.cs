using Automaton.Api.Entities;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ActionScheduleServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ValidateSchedule_RejectsPastOneTimeRun()
    {
        var schedule = Valid(ActionScheduleTypes.OneTime);
        schedule.RunAtUtc = Now.AddSeconds(-1);
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_RejectsTooSmallInterval()
    {
        var schedule = Valid(ActionScheduleTypes.Interval);
        schedule.IntervalSeconds = 299;
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_RejectsMalformedCron()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "not a cron";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_RejectsUnsupportedTimeZone()
    {
        var schedule = Valid(ActionScheduleTypes.Interval);
        schedule.TimeZone = "Mars/Olympus_Mons";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void CalculateNextRun_UsesCronTimeZoneAndReturnsUtc()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "0 9 * * *";
        schedule.TimeZone = "Europe/Paris";
        var next = ActionScheduleService.CalculateNextRun(schedule, Now);
        Assert.That(next, Is.EqualTo(new DateTime(2026, 1, 16, 8, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void CalculateNextRun_AdvancesIntervalFromRequestedInstant()
    {
        var schedule = Valid(ActionScheduleTypes.Interval);
        schedule.IntervalSeconds = 300;
        Assert.That(ActionScheduleService.CalculateNextRun(schedule, Now), Is.EqualTo(Now.AddMinutes(5)));
    }

    private static ActionSchedule Valid(string type) => new()
    {
        ScheduleType = type,
        RunAtUtc = Now.AddHours(1),
        IntervalSeconds = 300,
        CronExpression = "*/5 * * * *",
        TimeZone = "Etc/UTC"
    };
}
