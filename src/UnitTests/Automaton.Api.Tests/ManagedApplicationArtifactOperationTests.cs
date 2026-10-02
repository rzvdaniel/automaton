using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class ManagedApplicationArtifactOperationTests
{
    [Test]
    public async Task CallbackRetryReusesPinnedArtifactRequestAndTerminalResult()
    {
        await using var db = Db();
        var runner = CreateRunner();
        db.Runners.Add(runner); await db.SaveChangesAsync();
        var publisher = new Publisher();
        var operations = RunnerExecutionTestServices.Operations(db, publisher);
        var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        var request = Request(Guid.NewGuid(), Guid.NewGuid(), "[]");
        await operations.QueueManagedArtifactsAsync(id, runner.Id, owner, request, CancellationToken.None);
        await AssertPinnedRequestAsync(operations, runner, id);
        await ApplySuccessAsync(operations, runner, id, request);
        var before = (await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ProtectedRequest;
        await operations.QueueManagedArtifactsAsync(id, runner.Id, owner,
            Request(request.AppId, request.DeploymentTargetId, "changed"), CancellationToken.None);
        Assert.That(publisher.Count, Is.EqualTo(1));
        Assert.That(publisher.Command!.Management, Is.True);
        Assert.That((await db.RunnerExecutionOperations.AsNoTracking().SingleAsync()).ProtectedRequest, Is.EqualTo(before));
        await AssertTerminalReplayAsync(operations, runner, id);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.QueueManagedArtifactsAsync(id, runner.Id,
            Guid.NewGuid(), request, CancellationToken.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnotherOwnerOrWrongTargetCannotQueueArtifacts(bool wrongTarget)
    {
        await using var db = Db();
        var app = Guid.NewGuid(); var owner = Guid.NewGuid(); var runner = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateLive(app, runner);
        db.AppDeploymentTargets.Add(target);
        db.WebAppUsers.Add(new() { WebAppId = app, UserId = owner });
        await db.SaveChangesAsync();
        var gateway = new ManagedApplicationArtifactGateway(db, RunnerExecutionTestServices.Operations(db));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => gateway.SynchronizeAsync(runner,
            wrongTarget ? owner : Guid.NewGuid(), new(app, wrongTarget ? Guid.NewGuid() : target.Id, []),
            Guid.NewGuid(), CancellationToken.None));
        Assert.That(await db.RunnerExecutionOperations.CountAsync(), Is.Zero);
    }

    [Test]
    public void DeploymentIdentityIsStableAndScopedToRunner()
    {
        var deployment = Guid.NewGuid(); var runner = Guid.NewGuid();
        var first = ManagedApplicationArtifactGateway.OperationIdentity(deployment, runner);
        Assert.That(ManagedApplicationArtifactGateway.OperationIdentity(deployment, runner), Is.EqualTo(first));
        Assert.That(ManagedApplicationArtifactGateway.OperationIdentity(deployment, runner),
            Is.Not.EqualTo(ManagedApplicationArtifactGateway.OperationIdentity(deployment, Guid.NewGuid())));
    }

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Runner CreateRunner() => new()
    { Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "artifact-runner", RunnerUsers = [] };
    private static SynchronizeApplicationManagedArtifactsRequest Request(Guid app, Guid target, string content)
        => new(app, target, [new("index.json", System.Text.Encoding.UTF8.GetBytes(content))]);
    private static Task<RunnerExecutionReceipt> ApplySuccessAsync(RunnerExecutionOperationService operations, Runner runner,
        Guid id, SynchronizeApplicationManagedArtifactsRequest request)
        => operations.ApplyAsync(runner.ApiKeyId!.Value, id,
            new() { Outcome = "Succeeded", ManagedArtifactsResult = new(request.AppId, request.DeploymentTargetId, true) }, CancellationToken.None);
    private static async Task AssertPinnedRequestAsync(RunnerExecutionOperationService operations, Runner runner, Guid id)
    {
        var assignment = await operations.AssignmentAsync(runner.ApiKeyId!.Value, id, CancellationToken.None);
        Assert.That(assignment.Request!.ManagedArtifacts!.Files[0].Content, Is.EqualTo("[]"u8.ToArray()));
    }
    private static async Task AssertTerminalReplayAsync(RunnerExecutionOperationService operations, Runner runner, Guid id)
    {
        Assert.That((await operations.WaitAsync(id, CancellationToken.None)).ManagedArtifactsResult!.Success, Is.True);
        Assert.That((await operations.AssignmentAsync(runner.ApiKeyId!.Value, id, CancellationToken.None)).Disposition,
            Is.EqualTo("already-applied"));
    }
    private sealed class Publisher : IEventPublisher
    {
        public int Count { get; private set; }
        public RunnerExecutionRequestedEvent? Command { get; private set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        { Count++; Command = (RunnerExecutionRequestedEvent)(object)message; return Task.CompletedTask; }
    }
}
