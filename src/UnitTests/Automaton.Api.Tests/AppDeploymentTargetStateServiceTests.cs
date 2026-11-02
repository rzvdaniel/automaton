using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentTargetStateServiceTests
{
    [Test]
    public async Task ConcurrentRelationalClaimsForSameTarget_ProduceExactlyOneWinner()
    {
        var databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"deployment-target-claims-{Guid.NewGuid():N}.db");
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        try
        {
            var connectionString = CreateSqliteConnectionString(databasePath);
            await CreateSqliteTargetAsync(connectionString, appId, targetId);
            await AssertSingleConcurrentClaimAsync(connectionString, appId, targetId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static string CreateSqliteConnectionString(string databasePath)
        => new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
            Pooling = false
        }.ToString();

    private static async Task CreateSqliteTargetAsync(string connectionString, Guid appId, Guid targetId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE WebApps (Id TEXT NOT NULL PRIMARY KEY, DeletionClaimId TEXT NULL);
            INSERT INTO WebApps (Id) VALUES ($appId);
            CREATE TABLE AppDeploymentTargets (
                Id TEXT NOT NULL PRIMARY KEY,
                WebAppId TEXT NOT NULL,
                Status INTEGER NOT NULL,
                LastDeploymentAt TEXT NULL,
                ActiveDeploymentOperationId TEXT NULL,
                ActiveDeploymentExecutionAttemptId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            INSERT INTO AppDeploymentTargets (Id, WebAppId, Status, UpdatedAt)
            VALUES ($targetId, $appId, $status, $updatedAt);
            """;
        command.Parameters.AddWithValue("$targetId", targetId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$appId", appId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$status", (int)DeploymentTargetStatus.NotDeployed);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSingleConcurrentClaimAsync(
        string connectionString,
        Guid appId,
        Guid targetId)
    {
        await using var firstDb = CreateSqliteDbContext(connectionString);
        await using var secondDb = CreateSqliteDbContext(connectionString);
        var claims = await Task.WhenAll(
            TryStartAsync(new AppDeploymentTargetStateService(firstDb), appId, targetId),
            TryStartAsync(new AppDeploymentTargetStateService(secondDb), appId, targetId));

        Assert.Multiple(() =>
        {
            Assert.That(claims.Count(result => result), Is.EqualTo(1));
            Assert.That(claims.Count(result => !result), Is.EqualTo(1));
        });

        await using var verificationDb = CreateSqliteDbContext(connectionString);
        var status = await verificationDb.AppDeploymentTargets
            .Where(target => target.Id == targetId && target.WebAppId == appId)
            .Select(target => target.Status)
            .SingleAsync();
        Assert.That(status, Is.EqualTo(DeploymentTargetStatus.Deploying));
    }

    [Test]
    public void DeploymentContracts_ExposeImmutableTargetIdentity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(DeployAppPackageRequest).GetProperty(nameof(DeployAppPackageRequest.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployAppPackageResult).GetProperty(nameof(DeployAppPackageResult.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployPackageResponse).GetProperty(nameof(DeployPackageResponse.DeploymentTargetId)), Is.Not.Null);
        });
    }

    [Test]
    public void DeploymentTargetState_DoesNotExposeAmbiguousRuntimeMarker()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(AppDeploymentTarget).GetProperty("RuntimeDeployed"), Is.Null);
            Assert.That(
                typeof(AppDeploymentTarget).GetProperty(nameof(AppDeploymentTarget.GenericRuntimeDeployed)),
                Is.Not.Null);
            Assert.That(
                typeof(AppDeploymentTargetSummary).GetProperty("RuntimeDeployed"),
                Is.Null);
        });
    }

    private static WebApp AddAppWithTwoTargets(ApplicationDbContext db)
    {
        var appId = Guid.NewGuid();
        var app = new WebApp
        {
            Id = appId,
            Name = $"App {appId:N}",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets =
            {
                AppDeploymentTarget.CreateDefault(appId, Guid.NewGuid()),
                CreateNamedTarget(appId)
            }
        };
        db.WebApps.Add(app);
        return app;
    }

    private static AppDeploymentTarget CreateNamedTarget(Guid appId)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Server EU",
            Slug = "server-eu",
            ExecutionRunnerId = Guid.NewGuid(),
            Status = DeploymentTargetStatus.NotDeployed,
            CreatedAt = now,
            UpdatedAt = now
        };
        target.SynchronizeDefaultConstraint();
        return target;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"deployment-target-state-{Guid.NewGuid():N}")
                .Options);
    }

    private static ApplicationDbContext CreateSqliteDbContext(string connectionString)
    {
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connectionString)
                .Options);
    }

    private static async Task<bool> TryStartAsync(
        AppDeploymentTargetStateService service,
        Guid appId,
        Guid targetId)
    {
        try
        {
            await service.MarkStartedAsync(appId, targetId);
            return true;
        }
        catch (AppDeploymentTargetConflictException)
        {
            return false;
        }
    }

}
