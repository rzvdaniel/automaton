using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Messaging.Abstractions;
using Automaton.Worker.Services;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionScheduleProcessorTests
{
    [Test]
    public async Task CompetingWorkers_OnlyOneClaimsAndCreatesOccurrence()
    {
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(Schedule(ActionScheduleTypes.Interval));
        await using var firstDb = fixture.OpenContext();
        await using var secondDb = fixture.OpenContext();
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
        var scheduledFor = DateTime.UtcNow.AddMinutes(-1);
        var schedule = Schedule(ActionScheduleTypes.Interval);
        schedule.NextRunAtUtc = scheduledFor;
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule);
        await using var db = fixture.OpenContext();
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
        var schedule = Schedule(ActionScheduleTypes.Interval);
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule);
        var handler = new TestActionHandler();
        await using var db = fixture.OpenContext();
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
        var interceptor = new FailNextSaveInterceptor();
        var schedule = Schedule(ActionScheduleTypes.Interval);
        var scheduledFor = schedule.NextRunAtUtc;
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule, interceptor);
        interceptor.FailNextSave();
        await using var db = fixture.OpenContext();
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
        var schedule = Schedule(ActionScheduleTypes.Interval);
        var scheduledFor = schedule.NextRunAtUtc;
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule);
        await using var db = fixture.OpenContext();
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
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(Schedule(ActionScheduleTypes.Interval));
        await using var db = fixture.OpenContext();
        var processor = Processor(db);
        await processor.ProcessAsync(CancellationToken.None);
        await processor.ProcessAsync(CancellationToken.None);
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(), Is.EqualTo(1));
    }

    internal static ActionScheduleProcessor Processor(ApplicationDbContext db)
        => Processor(db, []);

    internal static ActionScheduleProcessor Processor(
        ApplicationDbContext db,
        IEnumerable<IWorkerScheduledActionHandler> handlers,
        ITransactionalEventExecutor? transactions = null)
        => new(db, new StubPublisher(), transactions ?? new StubTransaction(),
            new WorkerScheduledActionDispatcher(db, new StubTrustedValidator(), handlers),
            NullLogger<ActionScheduleProcessor>.Instance);

    internal static ActionSchedule Schedule(string type) => new()
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
