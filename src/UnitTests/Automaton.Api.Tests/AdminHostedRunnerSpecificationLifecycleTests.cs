using System.Data.Common;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AdminHostedRunnerSpecificationLifecycleTests
{
    [Test]
    public async Task ListWithOverlappingCountQueriesPreservesOrderingAndActiveRunnerCounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        var alpha = await SeedAsync(db);
        var controller = new AdminHostedRunnerSpecificationsController(db, null!);

        var response = await controller.List(CancellationToken.None);

        var result = (OkObjectResult)response.Result!;
        var rows = ((IReadOnlyList<HostedRunnerSpecificationDto>)result.Value!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(x => x.Name), Is.EqualTo(new[] { "Alpha", "Zulu" }));
            Assert.That(rows[0].Id, Is.EqualTo(alpha.Id));
            Assert.That(rows[0].RunnerCount, Is.EqualTo(2));
            Assert.That(rows[0].OutdatedRunnerCount, Is.EqualTo(1));
            Assert.That(rows[1].IsActive, Is.False);
            Assert.That(rows[1].RunnerCount, Is.Zero);
        });
    }

    [Test]
    public async Task DeleteWithOnlyRetainedDeletedRunnerReturnsConflictAndPreservesSpecification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        var alpha = await SeedAsync(db);
        await db.Database.ExecuteSqlRawAsync("UPDATE HostedRunners SET DeletedAt = '2026-01-01';");
        var controller = new AdminHostedRunnerSpecificationsController(db, null!);

        var response = await controller.Delete(alpha.Id, CancellationToken.None);

        Assert.That(response, Is.InstanceOf<ConflictObjectResult>());
        Assert.That(await db.HostedRunnerSpecifications.AnyAsync(x => x.Id == alpha.Id), Is.True);
    }

    [Test]
    public async Task DeleteUnusedSpecificationSucceedsWithoutRemovingReferencedSpecification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        var alpha = await SeedAsync(db);
        var unused = await db.HostedRunnerSpecifications.SingleAsync(x => x.Code == "zulu");
        var controller = new AdminHostedRunnerSpecificationsController(db, null!);

        var response = await controller.Delete(unused.Id, CancellationToken.None);

        Assert.That(response, Is.InstanceOf<NoContentResult>());
        Assert.That(await db.HostedRunnerSpecifications.AnyAsync(x => x.Id == unused.Id), Is.False);
        Assert.That(await db.HostedRunnerSpecifications.AnyAsync(x => x.Id == alpha.Id), Is.True);
    }

    private static ApplicationDbContext CreateDb(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection)
            .AddInterceptors(new DelayedRunnerCount()).Options);

    private static async Task<HostedRunnerSpecification> SeedAsync(ApplicationDbContext db)
    {
        // Only the relational tables exercised by this controller, including its retained-reference constraint.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE HostedRunnerSpecifications (
                Id TEXT PRIMARY KEY, Code TEXT, Name TEXT, Description TEXT, Revision INTEGER,
                SchemaVersion INTEGER, Resources TEXT, IsActive INTEGER, CreatedAtUtc TEXT, UpdatedAtUtc TEXT);
            CREATE TABLE HostedRunners (
                Id TEXT PRIMARY KEY, HostedRunnerSpecificationId TEXT, SpecificationRevision INTEGER, DeletedAt TEXT,
                FOREIGN KEY (HostedRunnerSpecificationId) REFERENCES HostedRunnerSpecifications(Id) ON DELETE RESTRICT);
            """);
        var alpha = new HostedRunnerSpecification { Id = Guid.NewGuid(), Code = "alpha", Name = "Alpha", Revision = 2 };
        db.HostedRunnerSpecifications.AddRange(alpha,
            new HostedRunnerSpecification { Id = Guid.NewGuid(), Code = "zulu", Name = "Zulu", IsActive = false });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO HostedRunners VALUES ({Guid.NewGuid()}, {alpha.Id}, 1, NULL);");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO HostedRunners VALUES ({Guid.NewGuid()}, {alpha.Id}, 2, NULL);");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO HostedRunners VALUES ({Guid.NewGuid()}, {alpha.Id}, 1, '2026-01-01');");
        return alpha;
    }

    private sealed class DelayedRunnerCount : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("COUNT(*)", StringComparison.Ordinal)
                && command.CommandText.Contains("HostedRunners", StringComparison.Ordinal))
                await Task.Delay(20, cancellationToken);
            return result;
        }
    }
}
