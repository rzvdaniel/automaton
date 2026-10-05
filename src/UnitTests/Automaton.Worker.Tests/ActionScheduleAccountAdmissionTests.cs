using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionScheduleAccountAdmissionTests
{
    [Test]
    public async Task AccountSchedules_ReservePublishedWorkAndRetryQueuedWorkAfterCompletion()
    {
        var ownerId = Guid.NewGuid();
        var first = ActionScheduleProcessorTests.Schedule(ActionScheduleTypes.OneTime);
        first.OwnerType = ActionScheduleOwnerTypes.User;
        first.OwnerUserId = ownerId;
        first.ExecutionScope = ActionExecutionScopes.AppRunner;
        var second = ActionScheduleProcessorTests.Schedule(ActionScheduleTypes.OneTime);
        second.OwnerType = first.OwnerType;
        second.OwnerUserId = ownerId;
        second.ExecutionScope = first.ExecutionScope;
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(first);
        await using var db = fixture.OpenContext();
        await ActionScheduleAccountFixture.SeedAsync(db, ownerId, first, second);
        var processor = ActionScheduleProcessorTests.Processor(db);
        await processor.ProcessAsync(CancellationToken.None);
        var pending = await db.ActionScheduleOccurrences.SingleAsync(x => x.Status == ActionScheduleOccurrenceStatuses.Published);
        var queued = await db.ActionScheduleOccurrences.SingleAsync(x => x.Status == ActionScheduleOccurrenceStatuses.Queued);
        Assert.That(pending.ExecutionId, Is.Null);

        // An unchanged retry must retain the queue while the dispatch is still unlinked.
        await processor.ProcessAsync(CancellationToken.None);
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(x => x.Status == ActionScheduleOccurrenceStatuses.Published), Is.EqualTo(1));
        await ActionScheduleAccountFixture.CompleteAsync(db, pending, first.ApplicationId);
        await processor.ProcessAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.That((await db.ActionScheduleOccurrences.SingleAsync(x => x.Id == queued.Id)).Status,
            Is.EqualTo(ActionScheduleOccurrenceStatuses.Published));
    }

}
