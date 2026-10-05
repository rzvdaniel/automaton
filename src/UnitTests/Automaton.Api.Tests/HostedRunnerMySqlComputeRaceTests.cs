using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class HostedRunnerMySqlComputeRaceTests
{
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task ConcurrentPlacementAndUpgradeCannotOvercommitHost(bool cpu, bool resize)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Set AUTOMATON_TEST_MYSQL_CONNECTION_STRING to a dedicated migrated test database.");
        await using var db = CreateContext(connectionString);
        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
        var fixture = HostedRunnerMySqlSeedData.SeedComputeFixture(db, cpu, resize);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = fixture.AccountIds.Select(accountId => AttemptAsync(connectionString, accountId, start.Task)).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);
        Assert.That(outcomes.Count(x => x == "admitted"), Is.EqualTo(1), string.Join(", ", outcomes));
        await AssertPersistedCapacityAsync(db, fixture.Host);
    }

    private static async Task AssertPersistedCapacityAsync(ApplicationDbContext db, RunnerHost host)
    {
        db.ChangeTracker.Clear();
        var occupying = await db.HostedRunners.AsNoTracking()
            .Where(x => x.RunnerHostId == host.Id && x.DeletedAt == null
                && (x.Status == HostedRunnerStatuses.Pending || x.Status == HostedRunnerStatuses.Running
                    || x.Status == HostedRunnerStatuses.Failed && x.ContainerId != null))
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(occupying.Sum(x => Math.Max(x.SpecificationSnapshot.Resources.CpuLimit,
                x.PendingSpecificationSnapshot?.Resources.CpuLimit ?? 0)), Is.LessThanOrEqualTo(host.TotalCpuCores));
            Assert.That(occupying.Sum(x => Math.Max(x.SpecificationSnapshot.Resources.MemoryLimitMb,
                x.PendingSpecificationSnapshot?.Resources.MemoryLimitMb ?? 0)), Is.LessThanOrEqualTo(host.TotalMemoryMb));
        });
    }

    private static async Task<string> AttemptAsync(string connectionString, Guid accountId, Task start)
    {
        await using var db = CreateContext(connectionString);
        var service = HostedRunnerProvisioningTestServiceFactory.Create(db, accountId, new HostedRunnerOptions { Enabled = false });
        await start;
        try
        {
            var preparation = await service.PrepareProvisioningAsync(accountId, Guid.Empty, forceProvision: true);
            return preparation.ShouldPublishProvisionRequest ? "admitted" : "capacity denied";
        }
        catch (Exception ex) when (IsDeadlock(ex))
        {
            // MySQL may abort a competing serializable transaction. An aborted request must reserve nothing.
            return "deadlock victim";
        }
    }

    private static bool IsDeadlock(Exception exception)
        => exception is MySqlException { Number: 1213 }
            || exception.InnerException is not null && IsDeadlock(exception.InnerException);

    private static ApplicationDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))).Options);


}
