using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class OfflineRunnerDeletionServiceTests
{
    [Test]
    public async Task OnlineRunner_IsRefusedAndNothingIsDeleted()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var owner = Guid.NewGuid();
        var runner = SeedRunner(db, owner);
        runner.ConnectionId = "connected";
        runner.LastHeartbeatAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, owner).DeleteAsync(runner.Id, CancellationToken.None));
        Assert.That(await db.Runners.AnyAsync(item => item.Id == runner.Id), Is.True);
    }

    [Test]
    public async Task UnknownRunner_ReturnsNull()
        => Assert.That(await Service(AppAuthorizationTestData.CreateDbContext(), Guid.NewGuid())
            .DeleteAsync(Guid.NewGuid(), CancellationToken.None), Is.Null);

    [Test]
    public async Task RunnerOfAnotherUser_IsRefusedBeforeAnyAppIsDeleted()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var runner = SeedRunner(db, Guid.NewGuid());
        var caller = Guid.NewGuid();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, caller, "tasks");
        (await db.AppDeploymentTargets.SingleAsync(target => target.WebAppId == appId)).ExecutionRunnerId = runner.Id;
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db, caller).DeleteAsync(runner.Id, CancellationToken.None));
        Assert.That(await db.WebApps.AnyAsync(app => app.Id == appId), Is.True);
        Assert.That(await db.Runners.AnyAsync(item => item.Id == runner.Id), Is.True);
    }

    [Test]
    public async Task AppTheCallerCannotDelete_RefusesTheWholeRequestBeforeDeletingAnything()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var caller = Guid.NewGuid();
        var runner = SeedRunner(db, caller);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, mine, caller, "mine");
        AppAuthorizationTestData.SeedApp(db, theirs, Guid.NewGuid(), "theirs");
        foreach (var target in db.AppDeploymentTargets) target.ExecutionRunnerId = runner.Id;
        await db.SaveChangesAsync();

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, caller).DeleteAsync(runner.Id, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("Nothing was deleted"));
        Assert.That(await db.WebApps.CountAsync(), Is.EqualTo(2));
        Assert.That(await db.Runners.AnyAsync(item => item.Id == runner.Id), Is.True);
    }

    private static OfflineRunnerDeletionService Service(Automaton.Api.Data.ApplicationDbContext db, Guid caller)
    {
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(caller));
        var policy = new RunnerDeletionPolicy(db, authorization);
        var deletion = new RunnerDeletionService(db, policy, new NoAudit(),
            AppAuthorizationTestData.CreateUserContextService(caller), RunnerExecutionTestServices.Revocation(db));
        return new OfflineRunnerDeletionService(db, null!, deletion, policy, authorization);
    }

    private sealed class NoAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

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
