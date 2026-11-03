using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionExecutionLockPostgresTests
{
    private PostgresTestDatabase database = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase() => database = await PostgresTestDatabase.CreateAsync();

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (database is not null) await database.DisposeAsync();
    }

    [Test]
    public async Task IndependentApiSessions_ExcludeConcurrentExecutorAndAllowRetryAfterSessionEnds()
    {
        await using var first = database.Open();
        await using var second = database.Open();
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
        await using var first = database.Open();
        await using var second = database.Open();
        var appId = Guid.NewGuid();
        await using var owner = await AppDeletionExecutionLock.AcquireAsync(first, appId);
        await owner.AssertOwnedAsync();
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync();
        // The advisory lock is the only one held, so its session is the executor's.
        await using var kill = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND granted AND pid <> pg_backend_pid()",
            observer);
        Assert.That(await kill.ExecuteScalarAsync(), Is.EqualTo(true));
        await using var retry = await AcquireWhenReleasedAsync(second, appId);
        await Assert.ThatAsync(() => owner.AssertOwnedAsync(), Throws.TypeOf<AppDeploymentTargetConflictException>());
        await retry.AssertOwnedAsync();
    }

    // PostgreSQL frees the lock when the terminated backend finishes exiting, a moment after the call returns.
    private static async Task<AppDeletionExecutionLock> AcquireWhenReleasedAsync(ApplicationDbContext db, Guid appId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await AppDeletionExecutionLock.AcquireAsync(db, appId); }
            catch (AppDeploymentTargetConflictException) when (attempt < 50) { await Task.Delay(100); }
        }
    }
}
