using Automaton.Api.Entities;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Automaton.Tests.Shared;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
[Category("PostgreSqlIntegration")]
public sealed class ActionSchedulePostgresRaceTests
{
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public async Task DistinctSchedulesAcrossWorkers_RespectAccountCapacityAndRecoverQueuedWork(int maximum, bool queued)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var connectionString = database.ConnectionString;
        await using var db = ActionSchedulePostgresRaceFixture.Open(connectionString);
        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
        var ownerId = await ActionSchedulePostgresRaceFixture.SeedAsync(db, maximum, queued);
        await RaceAsync(connectionString);
        await AssertCapacityAsync(db, ownerId, maximum);

        var published = await db.ActionScheduleOccurrences
            .FirstAsync(x => x.Schedule.OwnerUserId == ownerId && x.Status == ActionScheduleOccurrenceStatuses.Published);
        var appId = await db.ActionSchedules.Where(x => x.Id == published.ScheduleId).Select(x => x.ApplicationId).SingleAsync();
        await ActionScheduleAccountFixture.CompleteAsync(db, published, appId);
        // New contexts simulate Worker interruption/restart while pending dispatch has no execution link.
        await RaceAsync(connectionString);
        Assert.That(await ActionScheduleCapacityReservations.Active(db)
            .CountAsync(x => x.Schedule.OwnerUserId == ownerId), Is.EqualTo(maximum));
        Assert.That(await db.ActionScheduleOccurrences.CountAsync(x => x.Schedule.OwnerUserId == ownerId
            && x.Status == ActionScheduleOccurrenceStatuses.Queued), Is.EqualTo(2 - maximum));
    }

    private static async Task RaceAsync(string connectionString)
    {
        var barrier = new ActionSchedulePostgresRaceFixture.AdmissionBarrier();
        await using var firstDb = ActionSchedulePostgresRaceFixture.Open(connectionString);
        await using var secondDb = ActionSchedulePostgresRaceFixture.Open(connectionString);
        var first = ActionScheduleProcessorTests.Processor(firstDb, [], new ActionSchedulePostgresRaceFixture.Transactions(firstDb, barrier));
        var second = ActionScheduleProcessorTests.Processor(secondDb, [], new ActionSchedulePostgresRaceFixture.Transactions(secondDb, barrier));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await Task.WhenAll(first.ProcessAsync(timeout.Token), second.ProcessAsync(timeout.Token));
    }

    private static async Task AssertCapacityAsync(Automaton.Api.Data.ApplicationDbContext db, Guid ownerId, int maximum)
    {
        db.ChangeTracker.Clear();
        var occurrences = await db.ActionScheduleOccurrences.AsNoTracking()
            .Where(x => x.Schedule.OwnerUserId == ownerId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(occurrences, Has.Count.EqualTo(3));
            Assert.That(occurrences.Count(x => x.Status == ActionScheduleOccurrenceStatuses.Published), Is.EqualTo(maximum));
            Assert.That(occurrences.Count(x => x.Status == ActionScheduleOccurrenceStatuses.Queued), Is.EqualTo(3 - maximum));
            Assert.That(occurrences.Select(x => x.ScheduleId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(occurrences.All(x => x.ExecutionId == null), Is.True);
        });
    }
}
