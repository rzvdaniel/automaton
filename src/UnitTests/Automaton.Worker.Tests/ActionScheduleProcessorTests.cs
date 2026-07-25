using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Messaging.Abstractions;
using Automaton.Worker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionScheduleProcessorTests
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

    [Test]
    public async Task CompetingWorkers_OnlyOneClaimsOccurrence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE ActionSchedules (
                    Id TEXT PRIMARY KEY, OwnerType TEXT NOT NULL, OwnerUserId TEXT NULL,
                    ApplicationId TEXT NOT NULL, ActionKey TEXT NOT NULL, Name TEXT NOT NULL,
                    Description TEXT NULL, ExecutionScope TEXT NOT NULL, ScheduleType TEXT NOT NULL,
                    RunAtUtc TEXT NULL, IntervalSeconds INTEGER NULL, CronExpression TEXT NULL,
                    TimeZone TEXT NOT NULL, Enabled INTEGER NOT NULL, NextRunAtUtc TEXT NULL,
                    LastTriggeredAtUtc TEXT NULL, LastExecutionId TEXT NULL,
                    ConcurrencyPolicy TEXT NOT NULL, MisfirePolicy TEXT NOT NULL, InputJson TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL,
                    LeaseOwner TEXT NULL, LeaseExpiresAtUtc TEXT NULL
                );
                """);
            setup.ActionSchedules.Add(Schedule(ActionScheduleTypes.Interval));
            await setup.SaveChangesAsync();
        }
        await using var firstDb = new ApplicationDbContext(options);
        await using var secondDb = new ApplicationDbContext(options);
        var first = Processor(firstDb);
        var second = Processor(secondDb);
        var id = await firstDb.ActionSchedules.Select(x => x.Id).SingleAsync();
        var now = DateTime.UtcNow;
        var results = await Task.WhenAll(
            first.TryClaimAsync(id, now, CancellationToken.None),
            second.TryClaimAsync(id, now, CancellationToken.None));
        Assert.That(results.Count(x => x), Is.EqualTo(1));
    }

    [TestCase(ActionScheduleConcurrencyPolicies.Allow, true, false, ActionScheduleOccurrenceStatuses.Published)]
    [TestCase(ActionScheduleConcurrencyPolicies.SkipIfRunning, true, false, ActionScheduleOccurrenceStatuses.Skipped)]
    [TestCase(ActionScheduleConcurrencyPolicies.Queue, true, false, ActionScheduleOccurrenceStatuses.Queued)]
    [TestCase(ActionScheduleConcurrencyPolicies.Queue, false, false, ActionScheduleOccurrenceStatuses.Published)]
    [TestCase(ActionScheduleConcurrencyPolicies.Allow, false, true, ActionScheduleOccurrenceStatuses.Skipped)]
    public void DetermineOccurrenceStatus_EnforcesConcurrencyAndMisfirePolicies(
        string concurrencyPolicy,
        bool active,
        bool misfired,
        string expected)
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
        Assert.That(
            ActionScheduleProcessor.CreateIdempotencyKey(id, occurrence),
            Is.EqualTo($"schedule:{id}:{occurrence:O}"));
    }

    private static ActionScheduleProcessor Processor(ApplicationDbContext db)
        => new(db, new StubPublisher(), new StubTransaction(), [], NullLogger<ActionScheduleProcessor>.Instance);

    private static ActionSchedule Schedule(string type) => new()
    {
        Id = Guid.NewGuid(),
        OwnerType = ActionScheduleOwnerTypes.System,
        ApplicationId = Guid.NewGuid(),
        ActionKey = "test-action",
        Name = "Test",
        ExecutionScope = ActionExecutionScopes.Worker,
        ScheduleType = type,
        IntervalSeconds = 300,
        TimeZone = "Etc/UTC",
        Enabled = true,
        NextRunAtUtc = DateTime.UtcNow.AddMinutes(-1),
        ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.Allow,
        MisfirePolicy = ActionScheduleMisfirePolicies.RunOnceNow,
        InputJson = "{}"
    };

    private sealed class StubPublisher : IEventPublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    }

    private sealed class StubTransaction : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
