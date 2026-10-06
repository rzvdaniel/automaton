using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionExecutionLockMySqlTests
{
    [Test]
    public async Task IndependentApiSessions_ExcludeConcurrentExecutorAndAllowRetryAfterSessionEnds()
    {
        var options = Options();
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        // Real deletion has already queried authorization/app state on this context.
        await first.Database.ExecuteSqlRawAsync("SELECT 1");
        var appId = Guid.NewGuid();
        await using var owner = await AppDeletionExecutionLock.AcquireAsync(first, appId);
        await Assert.ThatAsync(() => AppDeletionExecutionLock.AcquireAsync(second, appId),
            Throws.TypeOf<AppDeploymentTargetConflictException>());
        await owner.DisposeAsync();
        await using var retry = await AppDeletionExecutionLock.AcquireAsync(second, appId);
        await retry.AssertOwnedAsync();
    }

    [Test]
    public async Task LostExecutorSession_ReleasesLockAndCannotContinueAsOwner()
    {
        var options = Options();
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var appId = Guid.NewGuid();
        await using var owner = await AppDeletionExecutionLock.AcquireAsync(first, appId);
        await using var observer = new MySqlConnection(first.Database.GetDbConnection().ConnectionString);
        await observer.OpenAsync();
        await using var lookup = new MySqlCommand("SELECT IS_USED_LOCK(@name)", observer);
        lookup.Parameters.AddWithValue("@name", $"automaton:app-delete:{appId:N}");
        var sessionId = Convert.ToUInt64(await lookup.ExecuteScalarAsync());
        await using var kill = new MySqlCommand($"KILL CONNECTION {sessionId}", observer);
        await kill.ExecuteNonQueryAsync();
        await using var retry = await AppDeletionExecutionLock.AcquireAsync(second, appId);
        await Assert.ThatAsync(() => owner.AssertOwnedAsync(), Throws.TypeOf<AppDeploymentTargetConflictException>());
        await retry.AssertOwnedAsync();
    }

    private static DbContextOptions<ApplicationDbContext> Options()
    {
        var connection = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Ignore("Requires an isolated MySQL test database.");
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 36))).Options;
    }
}
