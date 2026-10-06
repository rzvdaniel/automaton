using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Messaging.Tests;
using Automaton.Worker.Messaging;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionSchedulePublicationTests
{
    [Test]
    public async Task DueAppRunnerSchedule_UsesRealPublisherAndCommitsMatchingOccurrence()
    {
        var schedule = ActionScheduleProcessorTests.Schedule(ActionScheduleTypes.OneTime);
        schedule.ExecutionScope = ActionExecutionScopes.AppRunner;
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule);
        await using var db = fixture.OpenContext();
        var cap = new RecordingCapPublisher();

        await Processor(db, cap).ProcessAsync(CancellationToken.None);

        var occurrence = await db.ActionScheduleOccurrences.SingleAsync();
        AssertMatchingPublication(cap, schedule, occurrence);
    }

    private static void AssertMatchingPublication(
        RecordingCapPublisher cap, ActionSchedule schedule, ActionScheduleOccurrence occurrence)
    {
        Assert.That(cap.Messages, Has.Count.EqualTo(1));
        var sent = cap.Messages.Single();
        var message = sent.Message as ScheduledActionExecutionRequestedEvent;
        Assert.That(message, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(sent.Topic, Is.EqualTo(EventTopics.ScheduledActionExecutionRequested));
            Assert.That(occurrence.Status, Is.EqualTo(ActionScheduleOccurrenceStatuses.Published));
            Assert.That(message!.OccurrenceId, Is.EqualTo(occurrence.Id));
            Assert.That(message.ScheduleId, Is.EqualTo(schedule.Id));
            Assert.That(message.ScheduledForUtc, Is.EqualTo(occurrence.ScheduledForUtc));
            Assert.That(message.IdempotencyKey, Is.EqualTo(occurrence.IdempotencyKey));
        });
    }

    private static ActionScheduleProcessor Processor(ApplicationDbContext db, RecordingCapPublisher cap)
        => new(db, new CapEventPublisher(cap), new Transactions(db),
            new WorkerScheduledActionDispatcher(db, new UnexpectedWorkerDispatch(), []),
            NullLogger<ActionScheduleProcessor>.Instance);

    private sealed class Transactions(ApplicationDbContext db) : ITransactionalEventExecutor
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private sealed class UnexpectedWorkerDispatch : ITrustedSystemActionScheduleDispatchValidator
    {
        public Task<AppActionManifest> ValidateAsync(ActionSchedule schedule, CancellationToken cancellationToken)
            => throw new InvalidOperationException("AppRunner schedules must not dispatch through Worker handlers.");
    }
}
