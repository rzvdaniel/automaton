using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Models;

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

    [Test]
    public void ValidateSchedule_RejectsEveryMinuteCronWithFiveMinuteMinimum()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "* * * * *";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_AcceptsFiveMinuteCronWithFiveMinuteMinimum()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "*/5 * * * *";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.Nothing);
    }

    [Test]
    public void ValidateSchedule_RejectsIrregularCronWhoseClosestOccurrencesAreTooNear()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "0,1 * * * *";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 300, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_UsesStricterActionMinimumForCron()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "*/5 * * * *";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 600, Now), Throws.InvalidOperationException);
    }

    [Test]
    public void ValidateSchedule_AllowsMinuteCronAtAdministratorPlatformMinimum()
    {
        var schedule = Valid(ActionScheduleTypes.Cron);
        schedule.CronExpression = "* * * * *";
        Assert.That(() => ActionScheduleService.ValidateSchedule(schedule, 1, Now), Throws.Nothing);
    }

    [TestCase(ActionScheduleMisfirePolicies.CatchUp)]
    [TestCase("Unknown")]
    public void ValidateMisfirePolicy_RejectsUnsupportedPoliciesForEveryCaller(string policy)
        => Assert.That(() => ActionScheduleService.ValidateMisfirePolicy(policy), Throws.InvalidOperationException);

    [TestCase(ActionScheduleMisfirePolicies.Skip)]
    [TestCase(ActionScheduleMisfirePolicies.RunOnceNow)]
    public void ValidateMisfirePolicy_AcceptsBoundedPolicies(string policy)
        => Assert.That(() => ActionScheduleService.ValidateMisfirePolicy(policy), Throws.Nothing);

    [TestCase(ActionScheduleTypes.OneTime, true, false, false)]
    [TestCase(ActionScheduleTypes.Interval, false, true, false)]
    [TestCase(ActionScheduleTypes.Cron, false, false, true)]
    public void ApplyTimingFields_ClearsFieldsFromOtherScheduleTypes(
        string type,
        bool hasRunAt,
        bool hasInterval,
        bool hasCron)
    {
        var schedule = new ActionSchedule
        {
            RunAtUtc = Now,
            IntervalSeconds = 60,
            CronExpression = "* * * * *"
        };
        ActionScheduleService.ApplyTimingFields(schedule, new SaveActionScheduleRequest
        {
            ScheduleType = type,
            RunAtUtc = new DateTimeOffset(Now),
            IntervalSeconds = 300,
            CronExpression = "*/5 * * * *"
        });
        Assert.Multiple(() =>
        {
            Assert.That(schedule.RunAtUtc.HasValue, Is.EqualTo(hasRunAt));
            Assert.That(schedule.IntervalSeconds.HasValue, Is.EqualTo(hasInterval));
            Assert.That(schedule.CronExpression != null, Is.EqualTo(hasCron));
        });
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
