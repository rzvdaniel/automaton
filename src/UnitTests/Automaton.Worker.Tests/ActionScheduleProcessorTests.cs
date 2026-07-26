using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Messaging.Abstractions;
using Automaton.Worker.Services;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task CompetingWorkers_OnlyOneClaimsAndCreatesOccurrence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(Schedule(ActionScheduleTypes.Interval));
            await setup.SaveChangesAsync();
        }
        await using var firstDb = new ApplicationDbContext(options);
        await using var secondDb = new ApplicationDbContext(options);
        var first = Processor(firstDb);
        var second = Processor(secondDb);
        var id = await firstDb.ActionSchedules.Select(x => x.Id).SingleAsync();
        var now = DateTime.UtcNow;
        await Task.WhenAll(
            first.ProcessAsync(CancellationToken.None),
            second.ProcessAsync(CancellationToken.None));
        var schedule = await firstDb.ActionSchedules.SingleAsync(x => x.Id == id);
        var occurrenceCount = await firstDb.ActionScheduleOccurrences.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(occurrenceCount, Is.EqualTo(1));
            Assert.That(schedule.NextRunAtUtc, Is.GreaterThan(now));
            Assert.That(schedule.LeaseOwner, Is.Null);
            Assert.That(schedule.LeaseExpiresAtUtc, Is.Null);
        });
    }

    [Test]
    public async Task DueSystemSchedule_CreatesOccurrenceAdvancesAndReleasesLease()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var scheduledFor = DateTime.UtcNow.AddMinutes(-1);
        var schedule = Schedule(ActionScheduleTypes.Interval);
        schedule.NextRunAtUtc = scheduledFor;
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(schedule);
            await setup.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        await Processor(db).ProcessAsync(CancellationToken.None);
        var occurrence = await db.ActionScheduleOccurrences.SingleAsync();
        var persistedSchedule = await db.ActionSchedules.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(occurrence.ScheduledForUtc, Is.EqualTo(scheduledFor));
            Assert.That(persistedSchedule.LastTriggeredAtUtc, Is.Not.Null);
            Assert.That(persistedSchedule.NextRunAtUtc, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(persistedSchedule.LeaseOwner, Is.Null);
            Assert.That(persistedSchedule.LeaseExpiresAtUtc, Is.Null);
        });
    }

    [Test]
    public async Task DueSystemWorkerAction_IsDispatchedAfterOccurrenceIsPersisted()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var schedule = Schedule(ActionScheduleTypes.Interval);
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(schedule);
            await setup.SaveChangesAsync();
        }

        var handler = new TestActionHandler();
        await using var db = new ApplicationDbContext(options);
        var processor = Processor(db, [handler]);
        await processor.ProcessAsync(CancellationToken.None);
        await processor.ProcessAsync(CancellationToken.None);
        var occurrence = await db.ActionScheduleOccurrences.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handler.Calls, Is.EqualTo(1));
            Assert.That(occurrence.Status,
                Is.EqualTo(ActionScheduleOccurrenceStatuses.Dispatched));
        });
    }

    [Test]
    public async Task FailedOccurrencePersistence_DoesNotAdvanceScheduleAndReleasesLease()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new FailNextSaveInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        var schedule = Schedule(ActionScheduleTypes.Interval);
        var scheduledFor = schedule.NextRunAtUtc;
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(schedule);
            await setup.SaveChangesAsync();
        }

        interceptor.FailNextSave();
        await using var db = new ApplicationDbContext(options);
        await Processor(db).ProcessAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var persistedSchedule = await db.ActionSchedules.SingleAsync();
        var occurrenceCount = await db.ActionScheduleOccurrences.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(occurrenceCount, Is.Zero);
            Assert.That(persistedSchedule.NextRunAtUtc, Is.EqualTo(scheduledFor));
            Assert.That(persistedSchedule.LeaseOwner, Is.Null);
            Assert.That(persistedSchedule.LeaseExpiresAtUtc, Is.Null);
        });
    }

    [Test]
    public async Task PostClaimDisable_ReleasesOwnedLeaseWithoutCreatingOccurrenceOrAdvancingSchedule()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var schedule = Schedule(ActionScheduleTypes.Interval);
        var scheduledFor = schedule.NextRunAtUtc;
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(schedule);
            await setup.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        var processor = Processor(db, [], new DisableScheduleBeforeOperationTransaction(db, schedule.Id));
        await processor.ProcessAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var persistedSchedule = await db.ActionSchedules.SingleAsync();
        var occurrenceCount = await db.ActionScheduleOccurrences.CountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(persistedSchedule.Enabled, Is.False);
            Assert.That(persistedSchedule.NextRunAtUtc, Is.EqualTo(scheduledFor));
            Assert.That(persistedSchedule.LeaseOwner, Is.Null);
            Assert.That(persistedSchedule.LeaseExpiresAtUtc, Is.Null);
            Assert.That(occurrenceCount, Is.Zero);
        });
    }

    [Test]
    public async Task RepeatedPolling_DoesNotCreateDuplicateOccurrenceForSameScheduledRun()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.ActionSchedules.Add(Schedule(ActionScheduleTypes.Interval));
            await setup.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        var processor = Processor(db);
        await processor.ProcessAsync(CancellationToken.None);
        await processor.ProcessAsync(CancellationToken.None);
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(), Is.EqualTo(1));
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
        => Assert.That(
            () => ActionScheduleProcessor.ValidateWorkerHandler(
                new AppActionManifest { Metadata = new AppActionMetadata { Key = "worker-action" } },
                null),
            Throws.InvalidOperationException);

    [Test]
    public async Task CrashAfterHandlerBeforeTerminalSave_DoesNotInvokeHandlerTwice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new FailSecondSaveInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        var now = DateTime.UtcNow;
        var schedule = Schedule(ActionScheduleTypes.OneTime);
        schedule.ApplicationId = InfrastructureAppTrust.PlatformAppId;
        schedule.ActionKey = "worker-action";
        var occurrence = new ActionScheduleOccurrence
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            ScheduledForUtc = now,
            TriggeredAtUtc = now,
            IdempotencyKey = ActionScheduleProcessor.CreateIdempotencyKey(schedule.Id, now),
            Status = ActionScheduleOccurrenceStatuses.Published,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await using (var setup = new ApplicationDbContext(options))
        {
            await CreateDispatchTablesAsync(setup);
            setup.AddRange(schedule, occurrence);
            await setup.SaveChangesAsync();
        }
        interceptor.Reset();
        var handler = new CountingHandler();
        await using (var firstDb = new ApplicationDbContext(options))
        {
            var processor = Processor(firstDb, [handler]);
            Assert.That(
                async () => await processor.DispatchWorkerOccurrencesAsync(now, CancellationToken.None),
                Throws.TypeOf<SimulatedCrashException>());
        }

        await using (var recoveryDb = new ApplicationDbContext(options))
        {
            var processor = Processor(recoveryDb, [handler]);
            await processor.DispatchWorkerOccurrencesAsync(now.AddMinutes(3), CancellationToken.None);
            var execution = await recoveryDb.AppModuleExecutions.SingleAsync();
            var recovered = await recoveryDb.ActionScheduleOccurrences.SingleAsync();
            Assert.Multiple(() =>
            {
                Assert.That(handler.Calls, Is.EqualTo(1));
                Assert.That(execution.Status, Is.EqualTo(AppModuleExecutionStatuses.Lost));
                Assert.That(recovered.Status, Is.EqualTo(ActionScheduleOccurrenceStatuses.Failed));
            });
        }
    }

    private static ActionScheduleProcessor Processor(ApplicationDbContext db)
        => Processor(db, []);

    private static ActionScheduleProcessor Processor(
        ApplicationDbContext db,
        IEnumerable<IWorkerScheduledActionHandler> handlers,
        ITransactionalEventExecutor? transactions = null)
        => new(db, new StubPublisher(), transactions ?? new StubTransaction(), new StubTrustedValidator(), handlers, NullLogger<ActionScheduleProcessor>.Instance);

    private static Task CreateDispatchTablesAsync(ApplicationDbContext db)
        => db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE ActionSchedules (
                Id TEXT PRIMARY KEY, OwnerType TEXT NOT NULL, OwnerUserId TEXT NULL, SystemIdentity TEXT NULL,
                ApplicationId TEXT NOT NULL, ActionKey TEXT NOT NULL, Name TEXT NOT NULL,
                Description TEXT NULL, ExecutionScope TEXT NOT NULL, ScheduleType TEXT NOT NULL,
                RunAtUtc TEXT NULL, IntervalSeconds INTEGER NULL, CronExpression TEXT NULL,
                TimeZone TEXT NOT NULL, Enabled INTEGER NOT NULL, NextRunAtUtc TEXT NULL,
                LastTriggeredAtUtc TEXT NULL, LastExecutionId TEXT NULL,
                ConcurrencyPolicy TEXT NOT NULL, MisfirePolicy TEXT NOT NULL, InputJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL,
                LeaseOwner TEXT NULL, LeaseExpiresAtUtc TEXT NULL
            );
            CREATE TABLE ActionScheduleOccurrences (
                Id TEXT PRIMARY KEY, ScheduleId TEXT NOT NULL, ScheduledForUtc TEXT NOT NULL,
                TriggeredAtUtc TEXT NOT NULL, ExecutionId TEXT NULL, Attempt INTEGER NOT NULL,
                IdempotencyKey TEXT NOT NULL, Status TEXT NOT NULL, FailureSummary TEXT NULL,
                CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
            );
            CREATE TABLE AppModuleExecutions (
                Id TEXT PRIMARY KEY, HostedRunnerOperationId TEXT NULL, ScheduleOccurrenceId TEXT NULL,
                AppId TEXT NOT NULL, ModuleType TEXT NOT NULL, ModuleKey TEXT NOT NULL,
                RunnerId TEXT NULL, TriggerType TEXT NOT NULL, RequestedByUserId TEXT NULL,
                Status TEXT NOT NULL, DeploymentHash TEXT NULL, ExitCode INTEGER NULL,
                ErrorCode TEXT NULL, FailureSummary TEXT NULL, CreatedAtUtc TEXT NOT NULL,
                StartedAtUtc TEXT NULL, ExecutionDeadlineUtc TEXT NULL, CompletedAtUtc TEXT NULL,
                LogsAvailableUntilUtc TEXT NULL, ResultAvailableUntilUtc TEXT NULL, CorrelationId TEXT NULL
            );
            CREATE UNIQUE INDEX IX_AppModuleExecutions_ScheduleOccurrenceId
                ON AppModuleExecutions (ScheduleOccurrenceId);
            """);

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

    private sealed class DisableScheduleBeforeOperationTransaction(ApplicationDbContext db, Guid scheduleId)
        : ITransactionalEventExecutor
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            var schedule = await db.ActionSchedules.SingleAsync(x => x.Id == scheduleId, cancellationToken);
            schedule.Enabled = false;
            await db.SaveChangesAsync(cancellationToken);
            await operation(cancellationToken);
        }
    }

    private sealed class StubTrustedValidator : ITrustedSystemActionScheduleDispatchValidator
    {
        public Task<AppActionManifest> ValidateAsync(ActionSchedule schedule, CancellationToken cancellationToken)
            => Task.FromResult(new AppActionManifest
            {
                Metadata = new AppActionMetadata { Key = schedule.ActionKey },
                Spec = new AppActionSpec
                {
                    Schedulable = true,
                    AllowedExecutionScopes = [ActionExecutionScopes.Worker]
                }
            });
    }

    private sealed class CountingHandler : IWorkerScheduledActionHandler
    {
        public string ActionKey => "worker-action";
        public int Calls { get; private set; }

        public Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Multiple(() =>
            {
                Assert.That(context.ExecutionId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(context.OccurrenceId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(context.IdempotencyKey, Does.StartWith("schedule:"));
            });
            return Task.CompletedTask;
        }
    }

    private sealed class TestActionHandler : IWorkerScheduledActionHandler
    {
        public string ActionKey => "test-action";
        public int Calls { get; private set; }

        public Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailSecondSaveInterceptor : SaveChangesInterceptor
    {
        private int saves;

        public void Reset() => saves = 0;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            saves++;
            if (saves == 2) throw new SimulatedCrashException();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailNextSaveInterceptor : SaveChangesInterceptor
    {
        private bool shouldFail;

        public void FailNextSave() => shouldFail = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (shouldFail)
            {
                shouldFail = false;
                throw new SimulatedCrashException();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class SimulatedCrashException : Exception;
}
