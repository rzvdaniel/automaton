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
        await service.MarkCompletedAsync(app.Id, first.Id, success: true);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(first.GenericRuntimeDeployed, Is.False);
            Assert.That(first.LastDeploymentAt, Is.Not.Null);
            Assert.That(first.LastSuccessfulDeploymentAt, Is.Not.Null);
            Assert.That(second.Status, Is.EqualTo(DeploymentTargetStatus.NotDeployed));
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
    public async Task CompletionWithStaleOperationId_IsRejectedAndLeavesTargetDeploying()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);
        var activeOperationId = Guid.NewGuid();

        await service.MarkStartedAsync(app.Id, target.Id, deploymentOperationId: activeOperationId);

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await service.MarkCompletedAsync(
                app.Id,
                target.Id,
                success: true,
                genericRuntimeDeployed: true,
                deploymentOperationId: Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Deploying));
            Assert.That(target.ActiveDeploymentOperationId, Is.EqualTo(activeOperationId));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
        });
    }

    [Test]
    public async Task ResumingActiveOperation_IsAllowedForTheSameOperationId()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);
        var operationId = Guid.NewGuid();

        await service.MarkStartedAsync(app.Id, target.Id, deploymentOperationId: operationId);
        await service.MarkStartedAsync(app.Id, target.Id, deploymentOperationId: operationId);
        await service.MarkCompletedAsync(
            app.Id,
            target.Id,
            success: true,
            genericRuntimeDeployed: true,
            deploymentOperationId: operationId);

        Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
        Assert.That(target.GenericRuntimeDeployed, Is.True);
    }

    [Test]
    public async Task OlderExecutionAttempt_CannotCompleteAfterTakeover()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);
        var operationId = Guid.NewGuid();
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();

        await service.MarkStartedAsync(app.Id, target.Id, deploymentOperationId: operationId, executionAttemptId: firstAttemptId);
        await service.MarkStartedAsync(app.Id, target.Id, deploymentOperationId: operationId, executionAttemptId: secondAttemptId);

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await service.MarkCompletedAsync(
                app.Id,
                target.Id,
                success: true,
                genericRuntimeDeployed: true,
                deploymentOperationId: operationId,
                executionAttemptId: firstAttemptId));

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await service.MarkCompletedAsync(
                app.Id,
                target.Id,
                success: false,
                deploymentOperationId: operationId,
                executionAttemptId: firstAttemptId));

        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Deploying));
            Assert.That(target.ActiveDeploymentOperationId, Is.EqualTo(operationId));
            Assert.That(target.ActiveDeploymentExecutionAttemptId, Is.EqualTo(secondAttemptId));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
        });
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
                        ActiveDeploymentOperationId TEXT NULL,
                        ActiveDeploymentExecutionAttemptId TEXT NULL,
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

    [Test]
    public async Task SuccessfulGenericRuntimeDeployment_PersistsSeparatePromotionMarker()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        await db.SaveChangesAsync();
        var target = app.DeploymentTargets.Single(candidate => !candidate.IsDefault);
        var service = new AppDeploymentTargetStateService(db);
        await service.MarkStartedAsync(app.Id, target.Id);
        await service.MarkCompletedAsync(
            app.Id,
            target.Id,
            success: true,
            genericRuntimeDeployed: true);

        Assert.Multiple(() =>
        {
            Assert.That(target.GenericRuntimeDeployed, Is.True);
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
        target.GenericRuntimeDeployed = true;
        await db.SaveChangesAsync();

        await service.MarkStartedAsync(app.Id, target.Id);
        await service.MarkCompletedAsync(
            app.Id, target.Id, success: false);

        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
            Assert.That(target.LastDeploymentAt, Is.Not.Null);
            Assert.That(target.LastSuccessfulDeploymentAt, Is.Null);
        });
    }

    [Test]
    public async Task SuccessfulDeploymentWithoutRuntime_ClearsPreviousRuntimeRoute()
    {
        await using var db = CreateDbContext();
        var app = AddAppWithTwoTargets(db);
        var target = app.DeploymentTargets.Single(candidate => !candidate.IsDefault);
        target.GenericRuntimeDeployed = true;
        await db.SaveChangesAsync();
        var service = new AppDeploymentTargetStateService(db);

        await service.MarkStartedAsync(app.Id, target.Id);
        await service.MarkCompletedAsync(
            app.Id,
            target.Id,
            success: true,
            genericRuntimeDeployed: false);

        Assert.Multiple(() =>
        {
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
            Assert.That(target.LastSuccessfulDeploymentAt, Is.Not.Null);
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
                genericRuntimeDeployed: true));
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
