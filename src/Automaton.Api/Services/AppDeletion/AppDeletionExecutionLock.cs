using System.Collections.Concurrent;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Automaton.Api.Services;

/// <summary>A session-scoped deletion executor fence, released when the executor dies.</summary>
public sealed class AppDeletionExecutionLock : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TestLocks = new();
    private readonly NpgsqlConnection? connection;
    private readonly SemaphoreSlim? testLock;
    private readonly long key;
    private int disposed;

    private AppDeletionExecutionLock(Guid appId, NpgsqlConnection? connection, SemaphoreSlim? testLock)
    {
        AppId = appId;
        this.connection = connection;
        this.testLock = testLock;
        key = PostgresAdvisoryLockKey.From($"automaton:app-delete:{appId:N}");
    }

    public Guid AppId { get; }

    public static async Task<AppDeletionExecutionLock> AcquireAsync(
        ApplicationDbContext db, Guid appId, CancellationToken token = default)
    {
        if (!db.Database.IsRelational())
        {
            // The in-memory provider is a test fixture, not a distributed production store.
            var semaphore = TestLocks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(0, token)) throw ActiveDeletion();
            return new(appId, null, semaphore);
        }
        if (!db.Database.IsNpgsql())
            throw new NotSupportedException("Deletion executor recovery requires the PostgreSQL platform store.");

        var options = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The PostgreSQL connection is not configured."))
        {
            // Returning a session with an advisory lock to a pool must never preserve ownership.
            Pooling = false
        };
        var connection = new NpgsqlConnection(options.ConnectionString);
        var owner = new AppDeletionExecutionLock(appId, connection, null);
        try
        {
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", owner.key);
            if (await command.ExecuteScalarAsync(token) is not true) throw ActiveDeletion();
            return owner;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task AssertOwnedAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref disposed) != 0) throw ActiveDeletion();
        if (testLock is not null) return;
        if (connection?.State != System.Data.ConnectionState.Open) throw ActiveDeletion();
        try
        {
            await using var command = new NpgsqlCommand(PostgresAdvisoryLockKey.HeldBySessionSql, connection);
            command.Parameters.AddWithValue("classid", PostgresAdvisoryLockKey.ClassId(key));
            command.Parameters.AddWithValue("objid", PostgresAdvisoryLockKey.ObjectId(key));
            if (await command.ExecuteScalarAsync(token) is not true) throw ActiveDeletion();
        }
        catch (NpgsqlException) { throw ActiveDeletion(); }
        catch (System.IO.IOException) { throw ActiveDeletion(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (connection is not null) await connection.DisposeAsync();
        else testLock?.Release();
    }

    private static AppDeploymentTargetConflictException ActiveDeletion()
        => new("App deletion is active on another executor or ownership was lost. Retry after it stops.");
}
