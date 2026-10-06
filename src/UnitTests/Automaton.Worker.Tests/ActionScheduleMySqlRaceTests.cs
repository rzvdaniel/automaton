using Automaton.Api.Entities;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class ActionScheduleMySqlRaceTests
{
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public async Task DistinctSchedulesAcrossWorkers_RespectAccountCapacityAndRecoverQueuedWork(int maximum, bool queued)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Set AUTOMATON_TEST_MYSQL_CONNECTION_STRING to a dedicated migrated test database.");
        await using var db = ActionScheduleMySqlRaceFixture.Open(connectionString);
        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
        var ownerId = await ActionScheduleMySqlRaceFixture.SeedAsync(db, maximum, queued);
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
        var barrier = new ActionScheduleMySqlRaceFixture.AdmissionBarrier();
        await using var firstDb = ActionScheduleMySqlRaceFixture.Open(connectionString);
        await using var secondDb = ActionScheduleMySqlRaceFixture.Open(connectionString);
        var first = ActionScheduleProcessorTests.Processor(firstDb, [], new ActionScheduleMySqlRaceFixture.Transactions(firstDb, barrier));
        var second = ActionScheduleProcessorTests.Processor(secondDb, [], new ActionScheduleMySqlRaceFixture.Transactions(secondDb, barrier));
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
