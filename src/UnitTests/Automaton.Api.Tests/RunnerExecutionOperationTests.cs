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

    [TestCase("cleanup-app-resources")]
    [TestCase("cleanup-target-resources")]
    [TestCase("cleanup-app")]
    [TestCase("cleanup-target")]
    public async Task CleanupLeavesManagementConsumerFreeForNestedDockerOperations(string kind)
    {
        await using var db = Db();
        var publisher = new Publisher();
        var service = RunnerExecutionTestServices.Operations(db, publisher);
        await service.QueueAsync(Guid.NewGuid(), Guid.NewGuid(), new(null, null,
            AppActionCommand.Create(kind, new { })), CancellationToken.None);
        Assert.That(publisher.Command!.Management, Is.False);
    }

    [Test]
    public async Task ActionManagementAndCancellationUseSeparateQueueFromExecution()
    {
        await using var db = Db(); var publisher = new Publisher();
        var service = RunnerExecutionTestServices.Operations(db, publisher); var runner = Guid.NewGuid();
        var execution = await service.QueueAsync(runner, Guid.NewGuid(), new(null, null,
            AppActionCommand.Create("execute", new { })), CancellationToken.None);
        Assert.That(publisher.Command!.Management, Is.False);
        await service.QueueAsync(runner, Guid.NewGuid(), new(null, null,
            AppActionCommand.Create("read-logs", new { })), CancellationToken.None);
        Assert.That(publisher.Command.Management, Is.True);
        await service.CancelAsync(execution, CancellationToken.None);
        Assert.That(publisher.Command.Management, Is.True);
        Assert.That(publisher.Command.OperationId, Is.EqualTo(execution));
    }

    [Test]
    public async Task PayloadIsProtectedAndCommandContainsOnlyStableIdentities()
    {
        var queued = await QueueWorkflowOperationAsync();
        Assert.That(queued.ProtectedRequest, Does.Not.Contain("private-secret"));
        Assert.That(queued.Command, Is.EqualTo(new RunnerExecutionRequestedEvent(queued.OperationId, queued.RunnerId)));
    }

    private static async Task<QueuedWorkflowOperation> QueueWorkflowOperationAsync()
    {
        await using var db = Db();
        var publisher = new Publisher();
        var service = RunnerExecutionTestServices.Operations(db, publisher);
        var runnerId = Guid.NewGuid();
        var id = await service.QueueAsync(runnerId, Guid.NewGuid(), new(null,
            new() { Id = Guid.NewGuid(), Code = "private-secret", Language = "python" }), CancellationToken.None);
        var protectedRequest = (await db.RunnerExecutionOperations.SingleAsync()).ProtectedRequest;
        return new QueuedWorkflowOperation(id, runnerId, protectedRequest, publisher.Command);
    }

    private sealed record QueuedWorkflowOperation(
        Guid OperationId, Guid RunnerId, string ProtectedRequest, RunnerExecutionRequestedEvent? Command);


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
        var before = await ApplyWorkflowResultAsync(db, callback, runner.ApiKeyId.Value, id);
        await AssertCompletedReplayAsync(db, callback, runner.ApiKeyId.Value, id, before);
        await AssertWorkflowOutputAsync(initiating, id);
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
        await AssertCancellationAssignmentAsync(service, runner, id);
        await AssertNotCompletedAsync(db);
        var key = runner.ApiKeyId!.Value;
        await service.ApplyAsync(key, id, new() { Outcome = "Cancelled" }, CancellationToken.None);
        Assert.That((await service.AssignmentAsync(key, id, CancellationToken.None)).Disposition, Is.EqualTo("already-applied"));
    }

    [Test]
    public async Task OwnerKeyCannotExecuteUnboundRunner()
    {
        await using var db = Db();
        var actor = Guid.NewGuid(); var key = Guid.NewGuid(); var runner = Runner();
        runner.ApiKeyId = null;
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }];
        db.Runners.Add(runner); await db.SaveChangesAsync();
        var service = RunnerExecutionTestServices.Operations(db);
        var id = await service.QueueAsync(runner.Id, actor, new(Guid.NewGuid(), null), CancellationToken.None);
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

    [TestCase(RunnerPurposes.Managed, "Standard")]
    [TestCase(RunnerPurposes.Managed, "PrivilegedHost")]
    public async Task RetiredApplicationProfilesAreRejected(string purpose, string profile)
    {
        await using var db = Db(); var actor = Guid.NewGuid(); var runner = Runner();
        runner.Purpose = purpose; runner.ApplicationProfile = profile;
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }];
        db.Runners.Add(runner); await db.SaveChangesAsync();
        var admission = new RunnerExecutionAdmissionService(db, null!, null!);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.AuthorizeAsync(runner.Id, actor, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.AuthorizeAsync(runner.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task InfrastructureProfileRequiresAdministrator()
    {
        await using var db = Db(); var actor = Guid.NewGuid(); var runner = Runner(); runner.Purpose = "Infrastructure";
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }]; db.Runners.Add(runner); await db.SaveChangesAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RunnerExecutionAdmissionService(db, null!, null!)
            .AuthorizeAsync(runner.Id, actor, CancellationToken.None));
    }

    [Test]
    public async Task MachineManagementApiAdmissionRejectsRawWorkflowExecution()
    {
        await using var db = Db();
        var actor = Guid.NewGuid();
        var runner = Runner();
        runner.ApplicationProfile = RunnerApplicationProfiles.MachineManagement;
        runner.CapabilitiesJson = "[\"ApplicationHosting\",\"ManagedContainerExecution\",\"WorkflowExecution\"]";
        runner.RunnerUsers = [new() { RunnerId = runner.Id, UserId = actor }];
        db.Runners.Add(runner);
        await db.SaveChangesAsync();

        var admission = new RunnerExecutionAdmissionService(db, null!, null!);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            admission.AuthorizeAsync(runner.Id, actor, CancellationToken.None));
    }

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<string?> ApplyWorkflowResultAsync(ApplicationDbContext db, RunnerExecutionOperationService service, Guid keyId, Guid id)
    {
        Assert.That((await service.ApplyAsync(keyId, id, CompletedWorkflowResult(), CancellationToken.None)).Outcome, Is.EqualTo("accepted"));
        return (await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ResultJson;
    }
    private static async Task AssertCompletedReplayAsync(ApplicationDbContext db, RunnerExecutionOperationService service,
        Guid keyId, Guid id, string? before)
    {
        Assert.That((await service.ApplyAsync(keyId, id, new(), CancellationToken.None)).Outcome, Is.EqualTo("already-applied"));
        Assert.That((await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ResultJson, Is.EqualTo(before));
    }
    private static async Task AssertWorkflowOutputAsync(RunnerExecutionOperationService service, Guid id)
        => Assert.That((await service.WaitAsync(id, CancellationToken.None)).OutputVariables["output"], Is.EqualTo("value"));
    private static RunnerExecutionResult CompletedWorkflowResult() => new()
    { Outcome = "Succeeded", Output = "marker", Logs = ["log"], OutputVariables = new() { ["output"] = "value" } };
    private static async Task AssertCancellationAssignmentAsync(RunnerExecutionOperationService service, Runner runner, Guid id)
        => Assert.That((await service.AssignmentAsync(runner.ApiKeyId!.Value, id, CancellationToken.None)).Disposition,
            Is.EqualTo("cancel"));
    private static async Task AssertNotCompletedAsync(ApplicationDbContext db)
        => Assert.That((await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).CompletedAtUtc, Is.Null);
    private static Runner Runner() => new() { Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "test", Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement, LastHeartbeatAtUtc = DateTime.UtcNow, RunnerUsers = [] };
    private sealed class Publisher : IEventPublisher
    {
        public RunnerExecutionRequestedEvent? Command { get; private set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        { Command = (RunnerExecutionRequestedEvent)(object)message; return Task.CompletedTask; }
    }
}
