using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class ManagedApplicationArtifactExecutorTests
{
    private string root = null!;
    private string? previousRoot;
    private RunnerWorkspaceService workspace = null!;
    private ServiceProvider services = null!;
    private RunnerExecutionExecutor executor = null!;
    [SetUp]
    public void SetUp()
    {
        previousRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(Path.GetTempPath(), "automaton-managed-artifacts-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        workspace = new();
        services = new ServiceCollection().AddSingleton(workspace).BuildServiceProvider();
        executor = new(services);
    }
    [TearDown]
    public void TearDown()
    {
        executor.Dispose();
        services.Dispose();
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousRoot);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public async Task CompletedReplacementReplaysWithoutOverwritingNewerArtifacts()
    {
        var journal = new RunnerExecutionJournal(Path.Combine(root, "journal")); var id = Guid.NewGuid();
        var request = new SynchronizeApplicationManagedArtifactsRequest(Guid.NewGuid(), Guid.NewGuid(),
            [new("index.json", "[]"u8.ToArray())]);
        var payload = new RunnerExecutionRequest(null, null, ManagedArtifacts: request);
        var result = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, payload, CancellationToken.None);
        var file = Path.Combine(workspace.GetAppFlowArtifactsRoot(request.AppId, request.DeploymentTargetId), "index.json");
        Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("[]"));
        await File.WriteAllTextAsync(file, "newer");
        var replay = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, payload, CancellationToken.None);
        Assert.That(replay.ManagedArtifactsResult, Is.EqualTo(result.ManagedArtifactsResult));
        Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("newer"));
        journal.Forget(id);
        var stale = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id, payload, CancellationToken.None);
        Assert.That(stale.Outcome, Is.EqualTo("Lost"));
        Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("newer"));
    }

    [Test]
    public void InvalidIdentityOrCancellationCannotMutateArtifacts()
    {
        var artifactExecutor = new ManagedApplicationArtifactExecutor(workspace);
        Assert.Throws<InvalidDataException>(() => artifactExecutor.Execute(new(Guid.Empty, Guid.NewGuid(), []), CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => artifactExecutor.Execute(new(Guid.NewGuid(), Guid.NewGuid(), []), cancellation.Token));
    }

    [Test]
    public async Task AcceptedButInterruptedReplacementIsNotRepeated()
    {
        var journal = new RunnerExecutionJournal(Path.Combine(root, "journal")); var id = Guid.NewGuid();
        using (journal.Lock(id)) journal.Accept(id);
        var request = new SynchronizeApplicationManagedArtifactsRequest(Guid.NewGuid(), Guid.NewGuid(),
            [new("index.json", "[]"u8.ToArray())]);
        var result = await new RunnerExecutionProcessor(journal, executor).ProcessAsync(id,
            new(null, null, ManagedArtifacts: request), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo("Lost"));
        Assert.That(Directory.Exists(workspace.GetAppFlowArtifactsRoot(request.AppId, request.DeploymentTargetId)), Is.False);
    }
}
