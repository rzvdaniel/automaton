using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AccountStorageProjectMigrationTests
{
    [Test]
    public async Task Migration_AdoptsExistingIdsAndAllowsLazyAccountsAndSharedHostedRunnerIdentity()
    {
        using var connection = CreateOpenConnection();
        using var db = CreatePreMigrationDbContext(connection);
        var firstAccount = Guid.NewGuid();
        var secondAccount = Guid.NewGuid();
        var accountWithoutRunner = Guid.NewGuid();
        await AddAccountAsync(db, firstAccount);
        await AddAccountAsync(db, secondAccount);
        await AddAccountAsync(db, accountWithoutRunner);
        await AddHostedRunnerAsync(db, firstAccount, 1_234_567);
        await AddHostedRunnerAsync(db, secondAccount, 1_234_568);

        await db.Database.MigrateAsync();

        var firstProject = await GetProjectAsync(db, firstAccount);
        var secondProject = await GetProjectAsync(db, secondAccount);
        Assert.That(firstProject, Is.EqualTo(1_234_567));
        Assert.That(secondProject, Is.EqualTo(1_234_568));

        var lazyProject = await new AccountStorageProjectAllocationService(db).GetOrCreateAsync(accountWithoutRunner);
        Assert.That(lazyProject, Is.GreaterThan(0).And.Not.EqualTo(firstProject).And.Not.EqualTo(secondProject));

        var duplicateAccount = Guid.NewGuid();
        await AddAccountAsync(db, duplicateAccount);
        Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO AccountStorageAllocations (AccountId, StorageProjectId) VALUES ({0}, {1})",
            duplicateAccount.ToString(),
            firstProject));

        await AddHostedRunnerAsync(db, firstAccount, firstProject);
        Assert.That(await CountHostedRunnersAsync(db, firstAccount, firstProject), Is.EqualTo(2));
        Assert.That(await new AccountStorageProjectAllocationService(db).GetOrCreateAsync(firstAccount), Is.EqualTo(firstProject));
    }

    [Test]
    public async Task Migration_FailsBeforePermanentChangesWhenOneAccountHasConflictingLegacyIds()
    {
        using var connection = CreateOpenConnection();
        using var db = CreatePreMigrationDbContext(connection);
        var accountId = Guid.NewGuid();
        await AddAccountAsync(db, accountId);
        await AddHostedRunnerAsync(db, accountId, 1_234_567);
        await AddHostedRunnerAsync(db, accountId, 1_234_568);

        var exception = Assert.ThrowsAsync<SqliteException>(() => db.Database.MigrateAsync());

        Assert.That(exception!.Message, Does.Contain("CHECK constraint failed"));
        Assert.That(await TableExistsAsync(db, "AccountStorageAllocations"), Is.False);
        Assert.That(await db.Database.GetAppliedMigrationsAsync(), Does.Not.Contain("20260923051139_MoveStorageProjectIdentityToAccount"));
    }

    private static SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        connection.CreateCollation("ascii_general_ci", (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left, right));
        return connection;
    }

    private static ApplicationDbContext CreatePreMigrationDbContext(SqliteConnection connection)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, options => options.MigrationsAssembly(typeof(Automaton.Infrastructure.DependencyInjection).Assembly.FullName))
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);
        db.Database.ExecuteSqlRaw("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE TABLE AspNetUsers (Id TEXT COLLATE ascii_general_ci NOT NULL PRIMARY KEY)");
        db.Database.ExecuteSqlRaw("CREATE TABLE HostedRunners (Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, StorageProjectId INTEGER NULL)");
        db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_HostedRunners_StorageProjectId ON HostedRunners (StorageProjectId)");

        const string storageIdentityMigration = "20260923051139_MoveStorageProjectIdentityToAccount";
        var migrations = db.Database.GetMigrations().ToArray();
        var storageIdentityMigrationIndex = Array.IndexOf(migrations, storageIdentityMigration);
        Assert.That(storageIdentityMigrationIndex, Is.GreaterThanOrEqualTo(0));
        foreach (var migration in migrations.Take(storageIdentityMigrationIndex))
        {
            db.Database.ExecuteSqlRaw(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({0}, {1})",
                migration,
                "9.0.11");
        }

        return db;
    }

    private static Task<int> AddAccountAsync(ApplicationDbContext db, Guid accountId)
        => db.Database.ExecuteSqlRawAsync("INSERT INTO AspNetUsers (Id) VALUES ({0})", accountId.ToString());

    private static Task<int> AddHostedRunnerAsync(ApplicationDbContext db, Guid accountId, int? projectId)
        => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO HostedRunners (Id, UserId, StorageProjectId) VALUES ({0}, {1}, {2})",
            Guid.NewGuid().ToString(),
            accountId.ToString(),
            projectId.HasValue ? projectId.Value : DBNull.Value);

    private static Task<int> GetProjectAsync(ApplicationDbContext db, Guid accountId)
        => db.AccountStorageAllocations
            .Where(x => x.AccountId == accountId)
            .Select(x => x.StorageProjectId)
            .SingleAsync();

    private static async Task<bool> TableExistsAsync(ApplicationDbContext db, string tableName)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<int> CountHostedRunnersAsync(ApplicationDbContext db, Guid accountId, int projectId)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM HostedRunners WHERE UserId = $account AND StorageProjectId = $project";
        var accountParameter = command.CreateParameter();
        accountParameter.ParameterName = "$account";
        accountParameter.Value = accountId.ToString();
        command.Parameters.Add(accountParameter);
        var projectParameter = command.CreateParameter();
        projectParameter.ParameterName = "$project";
        projectParameter.Value = projectId;
        command.Parameters.Add(projectParameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
