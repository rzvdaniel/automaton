using System.Collections.Concurrent;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Automaton.Api.Services;

/// <summary>A session-scoped deletion executor fence, released when the executor dies.</summary>
public sealed class AppDeletionExecutionLock : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TestLocks = new();
    private readonly MySqlConnection? connection;
    private readonly SemaphoreSlim? testLock;
    private readonly string name;
    private int disposed;

    private AppDeletionExecutionLock(Guid appId, MySqlConnection? connection, SemaphoreSlim? testLock)
    {
        AppId = appId;
        this.connection = connection;
        this.testLock = testLock;
        name = $"automaton:app-delete:{appId:N}";
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
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.Ordinal) != true)
            throw new NotSupportedException("Deletion executor recovery requires the MySQL platform store.");

        var options = new MySqlConnectionStringBuilder(db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The MySQL connection is not configured."))
        {
            // Returning a session with an advisory lock to a pool must never preserve ownership.
            Pooling = false
        };
        var connection = new MySqlConnection(options.ConnectionString);
        var owner = new AppDeletionExecutionLock(appId, connection, null);
        try
        {
            await connection.OpenAsync(token);
            await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 0)", connection);
            command.Parameters.AddWithValue("@name", owner.name);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw ActiveDeletion();
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
            await using var command = new MySqlCommand("SELECT IS_USED_LOCK(@name) = CONNECTION_ID()", connection);
            command.Parameters.AddWithValue("@name", name);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw ActiveDeletion();
        }
        catch (MySqlException) { throw ActiveDeletion(); }
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
