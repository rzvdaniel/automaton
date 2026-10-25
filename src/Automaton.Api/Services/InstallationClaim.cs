using System.Data.Common;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Keep the first-administrator claim on the same session as its transaction.</summary>
internal sealed class InstallationClaim(ApplicationDbContext db) : IAsyncDisposable
{
    private bool ownsLock;

    public static async Task<InstallationClaim> AcquireAsync(ApplicationDbContext db, CancellationToken token)
    {
        var claim = new InstallationClaim(db);
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.Ordinal) != true) return claim;
        await db.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT GET_LOCK('automaton:first-administrator', 0)";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1)
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
            command.CommandText = "SELECT RELEASE_LOCK('automaton:first-administrator')";
            await command.ExecuteScalarAsync();
        }
        finally
        {
            ownsLock = false;
            await db.Database.CloseConnectionAsync();
        }
    }
}
