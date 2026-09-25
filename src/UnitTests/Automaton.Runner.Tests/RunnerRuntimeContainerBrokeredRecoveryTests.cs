using System.Net;
using System.Net.Sockets;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class RunnerRuntimeContainerBrokeredRecoveryTests
{
    [Test]
    public async Task BrokeredStartPortIsPersistedBeforeReadinessAndRecoveredOnRestart()
    {
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
        var assignedPort = GetFreePort();
        var runtime = new RunnerRuntimeContainerManagerTests.ScopedContainerRuntime(assignedPort);
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var manager = CreateManager(workspace, registry, runtime);
        await manager.StartAsync(CancellationToken.None);

        var apply = manager.ApplyAsync(appId, targetId, RunnerRuntimeContainerManagerTests.Contract(8080), candidateId: candidateId);
        var pending = await WaitForCandidatePortAsync(workspace, appId, targetId, assignedPort);
        Assert.That(pending.Candidate!.EffectivePort, Is.EqualTo(assignedPort));
        runtime.EnableReadiness();
        await apply;
        var ready = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(workspace, appId, targetId);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Created!.HostPort, Is.Zero);
            Assert.That(runtime.Created.CandidateId, Is.EqualTo(candidateId));
            Assert.That(ready.CandidateId, Is.EqualTo(candidateId));
            Assert.That(runtime.Scopes, Is.All.EqualTo(new ApplicationContainerScope(appId, targetId)));
            Assert.That(registry.IsPortRegistered(assignedPort), Is.True);
        });
        await manager.StopAsync(CancellationToken.None);

        await AssertRestartRecoveryAsync(workspace, runtime, appId, targetId, assignedPort);
    }

    private static async Task<RunnerRuntimeDeploymentMetadata> WaitForCandidatePortAsync(
        RunnerWorkspaceService workspace,
        Guid appId,
        Guid targetId,
        int assignedPort)
    {
        RunnerRuntimeDeploymentMetadata? pending = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(25);
            pending = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(workspace, appId, targetId);
            if (pending.Candidate?.EffectivePort == assignedPort)
                return pending;
        }

        Assert.Fail("The brokered port was not persisted while the container was waiting for readiness.");
        return pending!;
    }

    private static async Task AssertRestartRecoveryAsync(
        RunnerWorkspaceService workspace,
        RunnerRuntimeContainerManagerTests.ScopedContainerRuntime runtime,
        Guid appId,
        Guid targetId,
        int assignedPort)
    {
        runtime.InspectedPort = assignedPort + 1;
        var registry = new RunnerRuntimeRegistry();
        var replacement = CreateManager(workspace, registry, runtime);
        await replacement.StartAsync(CancellationToken.None);
        var recovered = await WaitForRecoveredPortAsync(workspace, registry, appId, targetId, assignedPort + 1);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.ContainerId, Is.EqualTo("brokered-container"));
            Assert.That(recovered.EffectivePort, Is.EqualTo(assignedPort + 1));
            Assert.That(registry.IsPortRegistered(assignedPort + 1), Is.True);
            Assert.That(runtime.CreateCalls, Is.EqualTo(1));
        });
        await replacement.StopAsync(appId, targetId);
    }

    private static async Task<RunnerRuntimeDeploymentMetadata> WaitForRecoveredPortAsync(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        Guid appId,
        Guid targetId,
        int port)
    {
        var recovered = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(workspace, appId, targetId);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            recovered = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(workspace, appId, targetId);
            if (recovered.ContainerId == "brokered-container"
                && recovered.EffectivePort == port
                && registry.IsPortRegistered(port))
                break;
            await Task.Delay(100);
        }

        return recovered;
    }

    private static RunnerRuntimeContainerManager CreateManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        RunnerRuntimeContainerManagerTests.ScopedContainerRuntime runtime)
        => new(workspace, registry, runtime, NullLogger<RunnerRuntimeContainerManager>.Instance);

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

internal sealed class RunnerRuntimeContainerTestWorkspace : IDisposable
{
    private readonly string? originalWorkspace;
    private readonly string workspaceRoot;

    private RunnerRuntimeContainerTestWorkspace()
    {
        originalWorkspace = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-container-manager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
    }

    public static RunnerRuntimeContainerTestWorkspace Create() => new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspace);
        if (Directory.Exists(workspaceRoot))
            Directory.Delete(workspaceRoot, recursive: true);
    }
}
