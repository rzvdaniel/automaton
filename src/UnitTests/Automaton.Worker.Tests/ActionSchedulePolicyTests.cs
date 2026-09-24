using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionSchedulePolicyTests
{
    [Test]
    public void Advance_OneTimeCompletesAndDisablesSchedule()
    {
        var schedule = Schedule(ActionScheduleTypes.OneTime);
        ActionScheduleProcessor.Advance(schedule, schedule.NextRunAtUtc!.Value, DateTime.UtcNow);
        Assert.Multiple(() =>
        {
            Assert.That(schedule.Enabled, Is.False);
            Assert.That(schedule.NextRunAtUtc, Is.Null);
        });
    }

    [Test]
    public void Advance_IntervalMovesPastNowWithoutDrift()
    {
        var scheduled = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var schedule = Schedule(ActionScheduleTypes.Interval);
        schedule.IntervalSeconds = 300;
        ActionScheduleProcessor.Advance(schedule, scheduled, scheduled.AddMinutes(12));
        Assert.That(schedule.NextRunAtUtc, Is.EqualTo(scheduled.AddMinutes(15)));
    }

    [Test]
    public void Advance_CronCalculatesNextFutureOccurrence()
    {
        var now = new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc);
        var schedule = Schedule(ActionScheduleTypes.Cron);
        schedule.CronExpression = "*/5 * * * *";
        ActionScheduleProcessor.Advance(schedule, now.AddMinutes(-2), now);
        Assert.That(schedule.NextRunAtUtc, Is.EqualTo(now.AddMinutes(3)));
    }

    [TestCase(ActionScheduleConcurrencyPolicies.Allow, true, false, ActionScheduleOccurrenceStatuses.Published)]
    [TestCase(ActionScheduleConcurrencyPolicies.SkipIfRunning, true, false, ActionScheduleOccurrenceStatuses.Skipped)]
    [TestCase(ActionScheduleConcurrencyPolicies.Queue, true, false, ActionScheduleOccurrenceStatuses.Queued)]
    [TestCase(ActionScheduleConcurrencyPolicies.Queue, false, false, ActionScheduleOccurrenceStatuses.Published)]
    [TestCase(ActionScheduleConcurrencyPolicies.Allow, false, true, ActionScheduleOccurrenceStatuses.Skipped)]
    public void DetermineOccurrenceStatus_EnforcesConcurrencyAndMisfirePolicies(
        string concurrencyPolicy, bool active, bool misfired, string expected)
    {
        var schedule = Schedule(ActionScheduleTypes.Interval);
        schedule.ConcurrencyPolicy = concurrencyPolicy;
        schedule.MisfirePolicy = ActionScheduleMisfirePolicies.Skip;
        Assert.That(ActionScheduleProcessor.DetermineOccurrenceStatus(schedule, active, misfired), Is.EqualTo(expected));
    }

    [Test]
    public void CreateIdempotencyKey_IsStablePerScheduledOccurrence()
    {
        var id = Guid.NewGuid();
        var occurrence = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc);
        Assert.That(ActionScheduleProcessor.CreateIdempotencyKey(id, occurrence),
            Is.EqualTo($"schedule:{id}:{occurrence:O}"));
    }

    [Test]
    public void QueuedOneTimeOccurrence_RemainsEligibleAfterScheduleAutoCompletes()
    {
        var schedule = Schedule(ActionScheduleTypes.OneTime);
        var occurrence = new ActionScheduleOccurrence { Status = ActionScheduleOccurrenceStatuses.Queued };
        ActionScheduleProcessor.Advance(schedule, schedule.NextRunAtUtc!.Value, DateTime.UtcNow);
        Assert.Multiple(() =>
        {
            Assert.That(schedule.Enabled, Is.False);
            Assert.That(ActionScheduleProcessor.IsQueuedDispatchEligible(occurrence), Is.True);
        });
    }

    [Test]
    public void ManuallySkippedQueuedOccurrence_IsNotEligible()
    {
        var occurrence = new ActionScheduleOccurrence { Status = ActionScheduleOccurrenceStatuses.Skipped };
        Assert.That(ActionScheduleProcessor.IsQueuedDispatchEligible(occurrence), Is.False);
    }

    [Test]
    public void MissingWorkerHandler_IsRejected()
        => Assert.That(() => WorkerScheduledActionDispatcher.ValidateWorkerHandler(
            new AppActionManifest { Metadata = new AppActionMetadata { Key = "worker-action" } }, null),
            Throws.InvalidOperationException);

    private static ActionSchedule Schedule(string type) => new()
    {
        Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.System, ApplicationId = Guid.NewGuid(),
        ActionKey = "test-action", Name = "Test", ExecutionScope = ActionExecutionScopes.Worker,
        ScheduleType = type, IntervalSeconds = 300, TimeZone = "Etc/UTC", Enabled = true,
        NextRunAtUtc = DateTime.UtcNow.AddMinutes(-1), ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.Allow,
        MisfirePolicy = ActionScheduleMisfirePolicies.RunOnceNow, InputJson = "{}"
    };
}
