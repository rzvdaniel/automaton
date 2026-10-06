using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionTargetClaimFenceTests
{
    [Test]
    public async Task StaleExecutorCannotReleaseTargetAfterClaimReplacement()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"delete-target-fence-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var staleClaimId = Guid.NewGuid();
        var currentClaimId = Guid.NewGuid();

        try
        {
            await CreateDatabaseAsync(connectionString, appId, targetId, currentClaimId);
            await using var db = CreateContext(connectionString);
            var service = new AppDeploymentTargetStateService(db);
            var seededTarget = await db.AppDeploymentTargets.AsNoTracking()
                .Where(target => target.Id == targetId)
                .Select(target => new { target.WebAppId, target.Status })
                .SingleAsync();
            Assert.Multiple(() =>
            {
                Assert.That(seededTarget.WebAppId, Is.EqualTo(appId));
                Assert.That(seededTarget.Status, Is.EqualTo(DeploymentTargetStatus.Deploying));
            });
            await service.ReleaseMutationAsync(appId, targetId, DeploymentTargetStatus.Failed,
                deletionClaimId: staleClaimId);
            Assert.That(await ReadStatusAsync(db, targetId), Is.EqualTo(DeploymentTargetStatus.Deploying));

            await service.ReleaseMutationAsync(appId, targetId, DeploymentTargetStatus.Failed,
                deletionClaimId: currentClaimId);
            Assert.That(await ReadStatusAsync(db, targetId), Is.EqualTo(DeploymentTargetStatus.Failed));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ApplicationDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options);

    private static Task<DeploymentTargetStatus> ReadStatusAsync(ApplicationDbContext db, Guid targetId) =>
        db.AppDeploymentTargets.AsNoTracking().Where(target => target.Id == targetId)
            .Select(target => target.Status).SingleAsync();

    private static async Task CreateDatabaseAsync(
        string connectionString, Guid appId, Guid targetId, Guid currentClaimId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE WebApps (Id TEXT NOT NULL PRIMARY KEY, DeletionClaimId TEXT NULL);
            INSERT INTO WebApps (Id, DeletionClaimId) VALUES ($appId, $claimId);
            CREATE TABLE AppDeploymentTargets (
                Id TEXT NOT NULL PRIMARY KEY,
                WebAppId TEXT NOT NULL,
                Status TEXT NOT NULL,
                ActiveDeploymentOperationId TEXT NULL,
                ActiveDeploymentExecutionAttemptId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            INSERT INTO AppDeploymentTargets (Id, WebAppId, Status, UpdatedAt)
            VALUES ($targetId, $appId, $status, $updatedAt);
            """;
        command.Parameters.AddWithValue("$targetId", targetId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$appId", appId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$claimId", currentClaimId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$status", DeploymentTargetStatus.Deploying.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }
}
