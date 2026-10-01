using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class RunnerExecutionOperationTests
{
    private static IDataProtectionProvider Protection() => new EphemeralDataProtectionProvider();

    [Test]
    public async Task PayloadIsProtectedAndCommandContainsOnlyStableIdentities()
    {
        await using var db = Db();
        var publisher = new Publisher();
        var service = RunnerExecutionTestServices.Operations(db, publisher);
        var runnerId = Guid.NewGuid();
        var id = await service.QueueAsync(runnerId, Guid.NewGuid(), new(null,
            new() { Id = Guid.NewGuid(), Code = "private-secret", Language = "python" }), CancellationToken.None);
        Assert.That((await db.RunnerExecutionOperations.SingleAsync()).ProtectedRequest, Does.Not.Contain("private-secret"));
        Assert.That(publisher.Command, Is.EqualTo(new RunnerExecutionRequestedEvent(id, runnerId)));
    }

    [Test]
    public async Task AnotherApiInstanceCanApplyResultAndCompletedReplayDoesNotRewriteIt()
    {
        await using var db = Db();
        var runner = Runner(); db.Runners.Add(runner); await db.SaveChangesAsync();
        var protection = Protection();
        var initiating = RunnerExecutionTestServices.Operations(db, protection: protection);
        var id = await initiating.QueueAsync(runner.Id, Guid.NewGuid(), new(Guid.NewGuid(), null), CancellationToken.None);
        var callback = RunnerExecutionTestServices.Operations(db, protection: protection);
        Assert.That((await callback.AssignmentAsync(runner.ApiKeyId!.Value, id, CancellationToken.None)).Disposition, Is.EqualTo("execute"));
        var result = new RunnerExecutionResult { Outcome = "Succeeded", Output = "marker", Logs = ["log"], OutputVariables = new() { ["output"] = "value" } };
        Assert.That((await callback.ApplyAsync(runner.ApiKeyId.Value, id, result, CancellationToken.None)).Outcome, Is.EqualTo("accepted"));
        var before = (await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ResultJson;
        Assert.That((await callback.ApplyAsync(runner.ApiKeyId.Value, id, new(), CancellationToken.None)).Outcome, Is.EqualTo("already-applied"));
        Assert.That((await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ResultJson, Is.EqualTo(before));
        Assert.That((await initiating.WaitAsync(id, CancellationToken.None)).OutputVariables["output"], Is.EqualTo("value"));
    }

    [Test]
    public async Task WrongRunnerCannotReadPayloadOrSubmitEvenAfterCompletion()
    {
        await using var db = Db(); var runner = Runner(); db.Runners.Add(runner); await db.SaveChangesAsync();
        var service = RunnerExecutionTestServices.Operations(db);
        var id = await service.QueueAsync(runner.Id, Guid.NewGuid(), new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AssignmentAsync(Guid.NewGuid(), id, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyAsync(Guid.NewGuid(), id, new(), CancellationToken.None));
        await service.ApplyAsync(runner.ApiKeyId!.Value, id, new(), CancellationToken.None);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AssignmentAsync(Guid.NewGuid(), id, CancellationToken.None));
    }

    [Test]
    public async Task CancellationIsPersistedAndDoesNotPretendThatWorkHasStopped()
    {
        await using var db = Db(); var runner = Runner(); db.Runners.Add(runner); await db.SaveChangesAsync();
        var service = RunnerExecutionTestServices.Operations(db);
        var id = await service.QueueAsync(runner.Id, Guid.NewGuid(), new(Guid.NewGuid(), null), CancellationToken.None);
        await service.CancelAsync(id, CancellationToken.None);
        Assert.That((await service.AssignmentAsync(runner.ApiKeyId!.Value, id, CancellationToken.None)).Disposition, Is.EqualTo("cancel"));
        Assert.That((await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).CompletedAtUtc, Is.Null);
        await service.ApplyAsync(runner.ApiKeyId.Value, id, new() { Outcome = "Cancelled" }, CancellationToken.None);
        Assert.That((await service.AssignmentAsync(runner.ApiKeyId.Value, id, CancellationToken.None)).Disposition, Is.EqualTo("already-applied"));
    }

    [Test]
    public async Task OwnerKeyCanExecuteUnboundByoRunnerButCannotBypassEnrolledKeyBinding()
    {
        await using var db = Db();
        var actor = Guid.NewGuid(); var key = Guid.NewGuid(); var runner = Runner();
        runner.ApiKeyId = null;
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }];
        db.Runners.Add(runner); await db.SaveChangesAsync();
        var service = RunnerExecutionTestServices.Operations(db);
        var id = await service.QueueAsync(runner.Id, actor, new(Guid.NewGuid(), null), CancellationToken.None);
        Assert.That((await service.AssignmentAsync(key, id, CancellationToken.None,
            authenticatedUserId: actor, runnerId: runner.Id)).Disposition, Is.EqualTo("execute"));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AssignmentAsync(key, id, CancellationToken.None,
            authenticatedUserId: Guid.NewGuid(), runnerId: runner.Id));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AssignmentAsync(key, id, CancellationToken.None,
            authenticatedUserId: actor, runnerId: Guid.NewGuid()));
        runner.ApiKeyId = Guid.NewGuid(); await db.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AssignmentAsync(key, id, CancellationToken.None,
            authenticatedUserId: actor, runnerId: runner.Id));
    }

    [Test]
    public async Task RevocationTerminatesUnfinishedWork()
    {
        await using var db = Db(); var service = RunnerExecutionTestServices.Operations(db);
        var runnerId = Guid.NewGuid();
        var id = await service.QueueAsync(runnerId, Guid.NewGuid(), new(Guid.NewGuid(), null), CancellationToken.None);
        await service.TerminateRunnerAsync(runnerId, CancellationToken.None);
        Assert.That((await service.WaitAsync(id, CancellationToken.None)).Outcome, Is.EqualTo("Lost"));
    }

    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.Standard)]
    [TestCase(RunnerPurposes.Managed, RunnerApplicationProfiles.PrivilegedHost)]
    public async Task SupportedApplicationProfilesNeedNoConnectionId(string purpose, string profile)
    {
        await using var db = Db(); var actor = Guid.NewGuid(); var runner = Runner();
        runner.Purpose = purpose; runner.ApplicationProfile = profile;
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }];
        db.Runners.Add(runner); await db.SaveChangesAsync();
        var admission = new RunnerExecutionAdmissionService(db, null!, null!);
        Assert.That(await admission.AuthorizeAsync(runner.Id, actor, CancellationToken.None), Is.EqualTo(actor));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.AuthorizeAsync(runner.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task InfrastructureProfileRequiresAdministrator()
    {
        await using var db = Db(); var actor = Guid.NewGuid(); var runner = Runner(); runner.Purpose = RunnerPurposes.Infrastructure;
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }]; db.Runners.Add(runner); await db.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RunnerExecutionAdmissionService(db, null!, null!)
            .AuthorizeAsync(runner.Id, actor, CancellationToken.None));
    }

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Runner Runner() => new() { Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "test", LastHeartbeatAtUtc = DateTime.UtcNow, RunnerUsers = [] };
    private sealed class Publisher : IEventPublisher
    {
        public RunnerExecutionRequestedEvent? Command { get; private set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        { Command = (RunnerExecutionRequestedEvent)(object)message; return Task.CompletedTask; }
    }
}
