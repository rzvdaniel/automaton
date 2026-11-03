using System.Data.Common;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Keep the first-administrator claim on the same session as its transaction.</summary>
internal sealed class InstallationClaim(ApplicationDbContext db) : IAsyncDisposable
{
    private static readonly long LockKey = PostgresAdvisoryLockKey.From("automaton:first-administrator");
    private bool ownsLock;

    public static async Task<InstallationClaim> AcquireAsync(ApplicationDbContext db, CancellationToken token)
    {
        var claim = new InstallationClaim(db);
        if (!db.Database.IsNpgsql()) return claim;
        await db.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock({LockKey})";
            if (await command.ExecuteScalarAsync(token) is not true)
                throw new InstallationSetupException("Setup is already running. Retry after it completes.");
            claim.ownsLock = true;
            return claim;
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!ownsLock) return;
        try
        {
            await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_advisory_unlock({LockKey})";
            await command.ExecuteScalarAsync();
        }
        finally
        {
            ownsLock = false;
            await db.Database.CloseConnectionAsync();
        }
    }
}
