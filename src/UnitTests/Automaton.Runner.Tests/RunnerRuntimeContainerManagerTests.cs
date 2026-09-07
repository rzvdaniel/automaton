using System.Net;
using System.Net.Sockets;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunnerRuntimeContainerManagerTests
{
    private string? originalWorkspace;
    private string workspaceRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        originalWorkspace = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-container-manager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspace);
        if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, true);
    }

    [Test]
    public async Task FirstDeploymentPersistsReadyCurrentContainer()
    {
        var fake = new FakeContainerRuntime();
        var (manager, workspace, appId, targetId) = CreateManager(fake);
        await manager.StartAsync(CancellationToken.None);

        var contract = Contract(19001);
        Assert.That(contract.Runtime.Command, Is.Not.Null);
        await manager.ApplyAsync(appId, targetId, contract);
        Assert.That(fake.StartCalls, Is.EqualTo(1));

        var metadata = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(metadata.ContainerId, Is.EqualTo("container-1"));
            Assert.That(metadata.Candidate, Is.Null);
        });
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task FailedCandidatePreservesPreviousCurrent()
    {
        var fake = new FakeContainerRuntime();
        var (manager, workspace, appId, targetId) = CreateManager(fake);
        await manager.StartAsync(CancellationToken.None);
        await manager.ApplyAsync(appId, targetId, Contract(19002));
        fake.StartReady = false;

        Assert.ThrowsAsync<TimeoutException>(async () => await manager.ApplyAsync(appId, targetId, Contract(19003)));

        var metadata = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.That(metadata.ContainerId, Is.EqualTo("container-1"));
        Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ExitedCurrentIsRemovedFromRegistryAndMarkedFailed()
    {
        var fake = new FakeContainerRuntime();
        var (manager, workspace, appId, targetId) = CreateManager(fake);
        await manager.StartAsync(CancellationToken.None);
        await manager.ApplyAsync(appId, targetId, Contract(19004));
        fake.Running["container-1"] = false;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(250);
            var current = await ReadMetadataAsync(workspace, appId, targetId);
            if (current.State == RuntimeReadinessState.Failed)
                break;
        }
        var metadata = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Failed));
        Assert.That(metadata.EffectivePort, Is.Null);
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task OccupiedRequestedPortUsesDifferentHostPortButKeepsContainerPort()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 19005);
        occupied.Start();
        var fake = new FakeContainerRuntime();
        var (manager, _, appId, targetId) = CreateManager(fake);
        await manager.StartAsync(CancellationToken.None);

        await manager.ApplyAsync(appId, targetId, Contract(19005));

        Assert.That(fake.LastRequest, Is.Not.Null);
        Assert.That(fake.LastRequest!.ContainerPort, Is.EqualTo(19005));
        Assert.That(fake.LastRequest.HostPort, Is.Not.EqualTo(19005));
        await manager.StopAsync(CancellationToken.None);
    }

    private (RunnerRuntimeContainerManager Manager, RunnerWorkspaceService Workspace, Guid AppId, Guid TargetId) CreateManager(FakeContainerRuntime fake)
    {
        var workspace = new RunnerWorkspaceService();
        return (new RunnerRuntimeContainerManager(workspace, new RunnerRuntimeRegistry(), fake, NullLogger<RunnerRuntimeContainerManager>.Instance), workspace, Guid.NewGuid(), Guid.NewGuid());
    }

    private static ApplicationContract Contract(int port) => new()
    {
        Runtime = new ApplicationRuntimeSpec
        {
            Image = "runtime:1",
            Port = port,
            Command = new ApplicationCommandSpec { Executable = "server" }
        },
        Health = new ApplicationHealthSpec { StartupTimeoutSeconds = 1 }
    };

    private static async Task<RunnerRuntimeDeploymentMetadata> ReadMetadataAsync(RunnerWorkspaceService workspace, Guid appId, Guid targetId)
        => System.Text.Json.JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private sealed class FakeContainerRuntime : IApplicationContainerRuntime
    {
        public bool StartReady { get; set; } = true;
        public Dictionary<string, bool> Running { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TcpListener> listeners = new(StringComparer.Ordinal);
        private int sequence;
        public int StartCalls { get; private set; }
        public DockerApplicationContainerRequest? LastRequest { get; private set; }

        public Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerImageIdentity(image, image + "@sha256:" + new string('a', 64)));

        public Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            LastRequest = request;
            var id = "container-" + (++sequence);
            Running[id] = true;
            if (StartReady)
            {
                var listener = new TcpListener(IPAddress.Loopback, request.HostPort);
                listener.Start();
                listeners[id] = listener;
            }
            return Task.FromResult(new DockerApplicationContainer(id, request.Name, new DockerImageIdentity(request.Image, request.Image + "@sha256:" + new string('a', 64))));
        }

        public Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        {
            Running[containerIdOrName] = false;
            if (listeners.Remove(containerIdOrName, out var listener)) listener.Stop();
            return Task.CompletedTask;
        }

        public Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
            => Task.FromResult(Running.TryGetValue(containerIdOrName, out var running) && running);

        public Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerContainerIdentity(containerIdOrName, Running.ContainsKey(containerIdOrName), IsRunningAsync(containerIdOrName).Result,
                new Dictionary<string, string>
                {
                    ["com.automaton.managed"] = "true",
                    ["com.automaton.app-id"] = "unknown",
                    ["com.automaton.deployment-target-id"] = "unknown",
                    ["com.automaton.runtime-kind"] = "generic"
                }));

        public Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
