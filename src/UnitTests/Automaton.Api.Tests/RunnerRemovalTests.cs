using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class RunnerRemovalTests
{
    [TestCase(DeploymentTargetStatus.NotDeployed)]
    [TestCase(DeploymentTargetStatus.Running)]
    [TestCase(DeploymentTargetStatus.Deploying)]
    public async Task PrepareRemoval_RejectsEveryAssignedEnvironment(DeploymentTargetStatus status)
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, owner, "tasks");
        var target = await db.AppDeploymentTargets.SingleAsync(target => target.WebAppId == appId);
        target.ExecutionRunnerId = runner.Id;
        target.Status = status;
        await db.SaveChangesAsync();
        var policy = Policy(db, owner);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunnerRemovalService(db, policy).PrepareAsync(runner.Id, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain(target.Name));
        Assert.That(runner.DeletionRequestedAtUtc, Is.Null);
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(runner.Id));
    }

    [Test]
    public async Task PreparedRemoval_BlocksNewDurableOperations()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        await new RunnerRemovalService(db, Policy(db, owner)).PrepareAsync(runner.Id, CancellationToken.None);
        var operations = RunnerExecutionTestServices.Operations(db);

        Assert.ThrowsAsync<InvalidOperationException>(() => QueueManagementAsync(operations, runner.Id, owner));
        Assert.That(await db.RunnerExecutionOperations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task PrepareRemoval_RejectsUnfinishedOperations()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        db.RunnerExecutionOperations.Add(new RunnerExecutionOperation {
            Id = Guid.NewGuid(), RunnerId = runner.Id, ProtectedRequest = "test-payload" });
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RunnerRemovalService(db, Policy(db, owner)).PrepareAsync(runner.Id, CancellationToken.None));
        Assert.That(runner.DeletionRequestedAtUtc, Is.Null);
    }

    [Test]
    public async Task PrepareRemoval_IsRepeatableAndBlocksAssignmentsAtPersistenceBoundary()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        var service = new RunnerRemovalService(db, Policy(db, owner));

        var first = await service.PrepareAsync(runner.Id, CancellationToken.None);
        var second = await service.PrepareAsync(runner.Id, CancellationToken.None);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(runner.DeletionRequestedAtUtc, Is.Not.Null);

        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, owner, "tasks");
        var target = await db.AppDeploymentTargets.SingleAsync(target => target.WebAppId == appId);
        target.ExecutionRunnerId = runner.Id;
        Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.That((await db.AppDeploymentTargets.SingleAsync(target => target.WebAppId == appId)).ExecutionRunnerId, Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task FinalRemoval_RequiresPreparationConfirmationAndOfflineRunner(bool prepared, bool confirmed)
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        runner.ConnectionId = "still-connected";
        runner.LastHeartbeatAtUtc = DateTime.UtcNow;
        if (prepared) runner.DeletionRequestedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var service = new RunnerDeletionService(db, Policy(db, owner), null!,
            AppAuthorizationTestData.CreateUserContextService(owner), null!);

        Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(runner.Id, CancellationToken.None, confirmed));
        Assert.That(await db.Runners.AnyAsync(item => item.Id == runner.Id), Is.True);
    }

    [Test]
    public async Task FinalRemoval_RevokesInstallationKeyAfterHostCleanup()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        var key = new ApiKey { Id = Guid.NewGuid(), Name = "Runner installation", HashedKey = "test-hash", KeyPrefix = "test" };
        db.ApiKeys.Add(key);
        runner.ApiKeyId = key.Id;
        runner.DeletionRequestedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var service = new RunnerDeletionService(db, Policy(db, owner), new Audit(),
            AppAuthorizationTestData.CreateUserContextService(owner), RunnerExecutionTestServices.Revocation(db));

        await service.DeleteAsync(runner.Id, CancellationToken.None, hostCleanupConfirmed: true);

        Assert.That(await db.Runners.AnyAsync(item => item.Id == runner.Id), Is.False);
        Assert.That((await db.ApiKeys.SingleAsync(item => item.Id == key.Id)).IsActive, Is.False);
    }

    [Test]
    public async Task PrepareRemoval_RejectsOtherOwners()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var runner = SeedRunner(db, Guid.NewGuid());
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new RunnerRemovalService(db, Policy(db, Guid.NewGuid())).PrepareAsync(runner.Id, CancellationToken.None));
        Assert.That(runner.DeletionRequestedAtUtc, Is.Null);
    }

    private sealed class Audit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static RunnerDeletionPolicy Policy(Automaton.Api.Data.ApplicationDbContext db, Guid owner)
        => new(db, new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(owner)));

    private static async Task QueueManagementAsync(RunnerExecutionOperationService operations, Guid runnerId, Guid owner)
        => await operations.QueueAsync(runnerId, owner,
            new(null, null, AppActionCommand.Create("list-modules", new { })), CancellationToken.None);

    private static Runner SeedRunner(Automaton.Api.Data.ApplicationDbContext db, Guid owner)
    {
        var runner = new Runner { Id = Guid.NewGuid(), Name = "Tasks server",
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            InstallationId = $"sh_{Guid.NewGuid():N}" };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = owner });
        db.SaveChanges();
        return runner;
    }
}
