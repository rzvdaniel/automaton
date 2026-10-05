using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
public sealed class ActionScheduleCapacityReservationTests
{
    [TestCase(ActionScheduleOccurrenceStatuses.Published, null, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatching, null, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Queued, null, 0)]
    [TestCase(ActionScheduleOccurrenceStatuses.Failed, null, 0)]
    [TestCase(ActionScheduleOccurrenceStatuses.Skipped, null, 0)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatched, null, 0)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatched, AppModuleExecutionStatuses.Created, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatched, AppModuleExecutionStatuses.Dispatching, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatched, AppModuleExecutionStatuses.Running, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Published, AppModuleExecutionStatuses.Running, 1)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatched, AppModuleExecutionStatuses.Succeeded, 0)]
    [TestCase(ActionScheduleOccurrenceStatuses.Dispatching, AppModuleExecutionStatuses.Failed, 0)]
    public async Task ReservationTracksDispatchAndExecutionLifecycle(string occurrenceStatus, string? executionStatus, int expected)
    {
        var ownerId = Guid.NewGuid();
        var schedule = Schedule(ownerId);
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(schedule);
        await using var db = fixture.OpenContext();
        var occurrence = Occurrence(schedule.Id, occurrenceStatus);
        if (executionStatus != null)
        {
            occurrence.ExecutionId = Guid.NewGuid();
            db.AppModuleExecutions.Add(new AppModuleExecution
            {
                Id = occurrence.ExecutionId.Value, ScheduleOccurrenceId = occurrence.Id,
                AppId = schedule.ApplicationId, Status = executionStatus
            });
        }
        db.ActionScheduleOccurrences.Add(occurrence);
        await db.SaveChangesAsync();

        var count = await ActionScheduleCapacityReservations.Active(db)
            .CountAsync(x => x.Schedule.OwnerUserId == ownerId);
        var active = await ActionScheduleCapacityReservations.Active(db)
            .AnyAsync(x => x.ScheduleId == schedule.Id);
        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(active, Is.EqualTo(expected != 0));
        });
    }

    [Test]
    public async Task PendingReservationsAreSharedAcrossSchedulesAndIsolatedAcrossAccounts()
    {
        var ownerId = Guid.NewGuid();
        var first = Schedule(ownerId);
        var second = Schedule(ownerId);
        var other = Schedule(Guid.NewGuid());
        await using var fixture = await ActionScheduleTestDatabase.CreateAsync(first);
        await using var db = fixture.OpenContext();
        db.ActionSchedules.AddRange(second, other);
        db.ActionScheduleOccurrences.AddRange(
            Occurrence(first.Id, ActionScheduleOccurrenceStatuses.Published),
            Occurrence(second.Id, ActionScheduleOccurrenceStatuses.Dispatching),
            Occurrence(other.Id, ActionScheduleOccurrenceStatuses.Published));
        await db.SaveChangesAsync();

        Assert.That(await ActionScheduleCapacityReservations.Active(db)
            .CountAsync(x => x.Schedule.OwnerUserId == ownerId), Is.EqualTo(2));
    }

    private static ActionSchedule Schedule(Guid ownerId) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = ownerId, ApplicationId = Guid.NewGuid(),
        ActionKey = "reservation-test", Name = "Reservation test", Enabled = false
    };

    private static ActionScheduleOccurrence Occurrence(Guid scheduleId, string status) => new()
    {
        Id = Guid.NewGuid(), ScheduleId = scheduleId, Status = status,
        ScheduledForUtc = DateTime.UtcNow, TriggeredAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        IdempotencyKey = Guid.NewGuid().ToString()
    };
}
