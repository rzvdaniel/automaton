using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionClaimRelationalTests
{
    [Test]
    public async Task AdmissionTransaction_SerializesClaimAndOnlyItsOwnerCanRelease()
    {
        var path = Path.Combine(Path.GetTempPath(), "deletion-claim-" + Guid.NewGuid() + ".db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options;
        try
        {
            await VerifyClaimFenceAsync(options);
        }
        finally { File.Delete(path); }
    }

    private static async Task VerifyClaimFenceAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var admissionDb = new ApplicationDbContext(options);
        await using var deletionDb = new ApplicationDbContext(options);
        var appId = Guid.NewGuid();
        // Only the two tiny tables exercised here, rather than bootstrapping the platform database.
        await admissionDb.Database.ExecuteSqlRawAsync("CREATE TABLE WebApps (Id TEXT PRIMARY KEY, DeletionClaimId TEXT NULL); CREATE TABLE AdmittedTargets (AppId TEXT);");
        await admissionDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO WebApps (Id) VALUES ({appId})");
        var admission = new AppDeletionClaimService(admissionDb);
        var claims = new AppDeletionClaimService(deletionDb);
        await using var transaction = await admission.OpenAdmissionAsync(appId, CancellationToken.None);
        var pending = Task.Run(() => claims.AcquireAsync(appId));
        await admissionDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AdmittedTargets (AppId) VALUES ({appId})");
        await transaction!.CommitAsync();
        var claim = await pending;
        await transaction.DisposeAsync();
        await AssertStaleWriteRejectedAsync(admissionDb, appId);
        Assert.That(await admissionDb.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM AdmittedTargets").SingleAsync(), Is.EqualTo(1));
        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => admission.OpenAdmissionAsync(appId, CancellationToken.None));
        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => claims.AcquireAsync(appId));
        await claims.ReleaseAsync(appId, Guid.NewGuid());
        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => admission.OpenAdmissionAsync(appId, CancellationToken.None));
        await claims.ReleaseAsync(appId, claim);
        await using var retry = await admission.OpenAdmissionAsync(appId, CancellationToken.None);
        await retry!.CommitAsync();
    }
    private static async Task AssertStaleWriteRejectedAsync(ApplicationDbContext db, Guid appId)
    {
        var staleApp = new WebApp { Id = appId };
        db.Attach(staleApp);
        db.Entry(staleApp).Property(app => app.DeletionClaimId).IsModified = true;
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.That(await db.WebApps.Where(app => app.Id == appId).Select(app => app.DeletionClaimId).SingleAsync(), Is.Not.Null);
    }

}
