using System.Net;
using System.Net.Sockets;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunnerRuntimeStopFirstUpdateTests
{
    private string? originalWorkspace;
    private string workspaceRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        originalWorkspace = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-stop-first", Guid.NewGuid().ToString("N"));
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
    public async Task StopFirstStopsTheOldContainerBeforeTheNewOneStartsAndThenRemovesIt()
    {
        var runtime = new LockingRuntime();
        var (manager, appId, targetId, _) = await DeployedAsync(runtime, RuntimeUpdateModes.StopFirst);

        await manager.ApplyAsync(appId, targetId, Contract(RuntimeUpdateModes.StopFirst));

        Assert.That(runtime.Events, Is.EqualTo(new[]
        {
            "create c1", "start c1", "create c2", "stop c1", "start c2", "remove c1"
        }));
        Assert.That(runtime.LockViolations, Is.Zero);
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task StopFirstStartsTheOldContainerAgainWhenTheNewOneFails()
    {
        var runtime = new LockingRuntime();
        var (manager, appId, targetId, registry) = await DeployedAsync(runtime, RuntimeUpdateModes.StopFirst);
        runtime.FailNextStart = true;

        Assert.ThrowsAsync<InvalidOperationException>(() => manager.ApplyAsync(appId, targetId, Contract(RuntimeUpdateModes.StopFirst)));

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Events, Is.EqualTo(new[]
            {
                "create c1", "start c1", "create c2", "stop c1", "start c2 (fails)", "remove c2", "start c1"
            }));
            Assert.That(runtime.IsRunning("c1"), Is.True, "the old version serves again");
            Assert.That(registry.TryGet(appId, targetId, out var registration) && registration.Port == runtime.PortOf("c1"), Is.True,
                "the gateway points at the port Docker gave the restarted container");
        });
        var metadata = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(new RunnerWorkspaceService(), appId, targetId);
        Assert.That((metadata.ContainerId, metadata.State, metadata.EffectivePort),
            Is.EqualTo(("c1", RuntimeReadinessState.Ready, (int?)runtime.PortOf("c1"))));
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task WithoutTheFieldTheOldContainerKeepsRunningUntilTheNewOneIsReady()
    {
        var runtime = new LockingRuntime();
        var (manager, appId, targetId, _) = await DeployedAsync(runtime, null);

        await manager.ApplyAsync(appId, targetId, Contract(null));

        Assert.That(runtime.Events, Is.EqualTo(new[]
        {
            "create c1", "start c1", "create c2", "start c2", "remove c1"
        }));
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task TheFirstDeploymentHasNothingToStop()
    {
        var runtime = new LockingRuntime();
        var (manager, _, _, _) = await DeployedAsync(runtime, RuntimeUpdateModes.StopFirst);
        Assert.That(runtime.Events, Is.EqualTo(new[] { "create c1", "start c1" }));
        await manager.StopAsync(CancellationToken.None);
    }

    private static async Task<(RunnerRuntimeContainerManager Manager, Guid AppId, Guid TargetId, RunnerRuntimeRegistry Registry)> DeployedAsync(
        LockingRuntime runtime, string? update)
    {
        var registry = new RunnerRuntimeRegistry();
        var manager = new RunnerRuntimeContainerManager(new RunnerWorkspaceService(), registry, runtime,
            NullLogger<RunnerRuntimeContainerManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        var (appId, targetId) = (Guid.NewGuid(), Guid.NewGuid());
        await manager.ApplyAsync(appId, targetId, Contract(update));
        return (manager, appId, targetId, registry);
    }

    private static ApplicationContract Contract(string? update) => new()
    {
        Runtime = new ApplicationRuntimeSpec
        {
            Image = "runtime:1", Port = 8080, Update = update,
            Command = new ApplicationCommandSpec { Executable = "server" }
        },
        Health = new ApplicationHealthSpec { StartupTimeoutSeconds = 1 }
    };

    /// <summary>Containers share one data folder lock: a second running container is a violation, like VictoriaLogs' flock.</summary>
    private sealed class LockingRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime, IScopedApplicationContainerRuntime
    {
        private readonly Dictionary<string, (bool Running, int Port, TcpListener? Listener)> containers = [];
        private int sequence;
        public List<string> Events { get; } = [];
        public int LockViolations { get; private set; }
        public bool FailNextStart { get; set; }

        public bool IsRunning(string id) => containers[id].Running;
        public int PortOf(string id) => containers[id].Port;

        public Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            var id = "c" + ++sequence;
            Events.Add("create " + id);
            containers[id] = (false, 0, null);
            return Task.FromResult(new DockerApplicationContainer(id, request.Name, new(request.Image, request.Image + "@sha256:" + new string('a', 64))));
        }

        public Task<ApplicationContainerStartResult> StartApplicationAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            var fails = FailNextStart && id == "c2";
            if (containers.Values.Any(c => c.Running)) LockViolations++;
            if (fails)
            {
                FailNextStart = false;
                Events.Add("start c2 (fails)");
                throw new InvalidOperationException("cannot acquire lock on file flock.lock");
            }
            Events.Add("start " + id);
            // Docker assigns a fresh host port at every start.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            containers[id] = (true, port, listener);
            return Task.FromResult(new ApplicationContainerStartResult(true, "127.0.0.1", port, 8080));
        }

        public Task StopAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Events.Add("stop " + id);
            containers[id].Listener?.Stop();
            containers[id] = (false, containers[id].Port, null);
            return Task.CompletedTask;
        }

        public Task StopAndRemoveAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Events.Add("remove " + id);
            if (containers.Remove(id, out var state)) state.Listener?.Stop();
            return Task.CompletedTask;
        }

        public Task<DockerContainerIdentity> InspectAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            var exists = containers.TryGetValue(id, out var state);
            var labels = new Dictionary<string, string>
            {
                ["com.automaton.managed"] = "true", ["com.automaton.runtime-kind"] = "generic",
                ["com.automaton.app-id"] = scope.AppId.ToString(), ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString()
            };
            return Task.FromResult(new DockerContainerIdentity(id, exists, exists && state.Running, labels, "127.0.0.1", state.Port, 8080));
        }

        public async Task<bool> IsRunningAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
            => (await InspectAsync(scope, id, cancellationToken)).Running;

        public Task<string> GetLogsAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartApplicationAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAndRemoveAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsRunningAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DockerContainerIdentity> InspectAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetLogsAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
