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
    public async Task CompletingOneTarget_DoesNotChangeAnotherTarget()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var first = app.DeploymentTargets.Single(target => target.IsDefault);
        var second = app.DeploymentTargets.Single(target => !target.IsDefault);
        var service = new AppDeploymentTargetStateService(db);

        await service.MarkStartedAsync(app.Id, first.Id);
        await service.MarkCompletedAsync(
            app.Id, first.Id, success: true, deployFrontend: true, deployBackend: false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(first.FrontendDeployed, Is.True);
            Assert.That(first.BackendDeployed, Is.False);
            Assert.That(first.LastDeploymentAt, Is.Not.Null);
            Assert.That(first.LastSuccessfulDeploymentAt, Is.Not.Null);
            Assert.That(second.Status, Is.EqualTo(DeploymentTargetStatus.NotDeployed));
            Assert.That(second.FrontendDeployed, Is.False);
            Assert.That(second.BackendDeployed, Is.False);
            Assert.That(second.LastDeploymentAt, Is.Null);
            Assert.That(second.LastSuccessfulDeploymentAt, Is.Null);
        });
    }

    [Test]
    public async Task StartingSecondDeploymentForSameTarget_IsRejected()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);

        await service.MarkStartedAsync(app.Id, target.Id);

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await service.MarkStartedAsync(app.Id, target.Id));
        Assert.That(exception!.Message, Does.Contain("already active"));
    }

    [Test]
    public async Task DifferentTargets_CanBothHaveActiveDeployments()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var service = new AppDeploymentTargetStateService(db);

        foreach (var target in app.DeploymentTargets)
        {
            await service.MarkStartedAsync(app.Id, target.Id);
        }

        Assert.That(
            app.DeploymentTargets.All(target => target.Status == DeploymentTargetStatus.Deploying),
            Is.True);
    }

    [Test]
    public async Task ConcurrentRelationalClaimsForSameTarget_ProduceExactlyOneWinner()
    {
        var databasePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"deployment-target-claims-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
            Pooling = false
        }.ToString();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        try
        {
            await using (var setupConnection = new SqliteConnection(connectionString))
            {
                await setupConnection.OpenAsync();
                await using var createCommand = setupConnection.CreateCommand();
                createCommand.CommandText =
                    """
                    CREATE TABLE AppDeploymentTargets (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WebAppId TEXT NOT NULL,
                        Status INTEGER NOT NULL,
                        LastDeploymentAt TEXT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    INSERT INTO AppDeploymentTargets (Id, WebAppId, Status, UpdatedAt)
                    VALUES ($targetId, $appId, $status, $updatedAt);
                    """;
                createCommand.Parameters.AddWithValue("$targetId", targetId.ToString().ToUpperInvariant());
                createCommand.Parameters.AddWithValue("$appId", appId.ToString().ToUpperInvariant());
                createCommand.Parameters.AddWithValue("$status", (int)DeploymentTargetStatus.NotDeployed);
                createCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow);
                await createCommand.ExecuteNonQueryAsync();
            }

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
                Assert.That(
                    await verificationDb.AppDeploymentTargets
                        .Where(target => target.Id == targetId && target.WebAppId == appId)
                        .Select(target => target.Status)
                        .SingleAsync(),
                    Is.EqualTo(DeploymentTargetStatus.Deploying));
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task SuccessfulDeployment_PersistsRequestedComponents(
        bool deployFrontend,
        bool deployBackend)
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => !candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);

        await service.MarkStartedAsync(app.Id, target.Id);
        await service.MarkCompletedAsync(
            app.Id, target.Id, success: true, deployFrontend, deployBackend);

        Assert.Multiple(() =>
        {
            Assert.That(target.FrontendDeployed, Is.EqualTo(deployFrontend));
            Assert.That(target.BackendDeployed, Is.EqualTo(deployBackend));
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
        });
    }

    [Test]
    public async Task FailedDeployment_PersistsFailureWithoutClaimingComponents()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => !candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);

        await service.MarkStartedAsync(app.Id, target.Id);
        await service.MarkCompletedAsync(
            app.Id, target.Id, success: false, deployFrontend: true, deployBackend: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(target.FrontendDeployed, Is.False);
            Assert.That(target.BackendDeployed, Is.False);
            Assert.That(target.LastDeploymentAt, Is.Not.Null);
            Assert.That(target.LastSuccessfulDeploymentAt, Is.Null);
        });
    }

    [Test]
    public async Task Completion_ForTargetFromAnotherApp_IsRejected()
    {
        await using var db = CreateDbContext();
        var firstApp = AddAppWithTwoTargets(db);
        var secondApp = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var secondTarget = secondApp.DeploymentTargets.Single(candidate => candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.MarkCompletedAsync(
                firstApp.Id,
                secondTarget.Id,
                success: true,
                deployFrontend: true,
                deployBackend: true));
    }

    [Test]
    public void DeploymentContracts_ExposeImmutableTargetIdentity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(DeployAppPackageRequest).GetProperty(nameof(DeployAppPackageRequest.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployAppPackageResult).GetProperty(nameof(DeployAppPackageResult.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployAppResponse).GetProperty(nameof(DeployAppResponse.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployPackageResponse).GetProperty(nameof(DeployPackageResponse.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployBackendResponse).GetProperty(nameof(DeployBackendResponse.DeploymentTargetId)), Is.Not.Null);
            Assert.That(typeof(DeployBackendRequest).GetProperty(nameof(DeployBackendRequest.TargetSlug)), Is.Not.Null);
            Assert.That(typeof(AppControllerUploadResult).GetProperty(nameof(AppControllerUploadResult.DeploymentTargetId)), Is.Not.Null);
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
