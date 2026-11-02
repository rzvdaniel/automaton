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
