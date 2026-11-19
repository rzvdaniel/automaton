using System.Net;
using System.Net.Sockets;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

/// <summary>Docker stops app containers on a host reboot (restart policy `no`); the Runner starts them again and never removes one it cannot adopt.</summary>
[TestFixture]
[NonParallelizable]
public sealed class RunnerRuntimeRebootRecoveryTests
{
    private string? originalWorkspace;
    private string workspaceRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        originalWorkspace = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-reboot-recovery", Guid.NewGuid().ToString("N"));
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
    public async Task DeployedContainerGetsTheRestartPolicyAfterPromotion()
    {
        var (fake, registry, workspace, appId, targetId) = Setup();
        var manager = Manager(fake, registry, workspace);
        await manager.StartAsync(CancellationToken.None);

        await manager.ApplyAsync(appId, targetId, RunnerRuntimeContainerManagerTests.Contract(fake.NextPort));

        Assert.That(fake.AutoRestart, Is.EqualTo(new[] { "container-1" }), "Only the promoted container; candidates never auto-restart.");
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task StoppedOwnedContainerIsStartedNotRecreatedAndItsNewPortIsRegistered()
    {
        var (fake, registry, workspace, appId, targetId) = Setup();
        var first = Manager(fake, registry, workspace);
        await first.StartAsync(CancellationToken.None);
        await first.ApplyAsync(appId, targetId, RunnerRuntimeContainerManagerTests.Contract(fake.NextPort));
        await first.StopAsync(CancellationToken.None);
        fake.Reboot();

        var registryAfter = new RunnerRuntimeRegistry();
        var restarted = Manager(fake, registryAfter, workspace);
        await restarted.StartAsync(CancellationToken.None);
        await WaitAsync(() => registryAfter.Snapshot().Count == 1);

        var metadata = await RunnerRuntimeContainerManagerTests.ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(fake.CreateCalls, Is.EqualTo(1), "No new container.");
            Assert.That(fake.Removed, Is.Empty);
            Assert.That(fake.StartedExisting, Is.EqualTo(new[] { "container-1" }));
            Assert.That(metadata.ContainerId, Is.EqualTo("container-1"));
            Assert.That(metadata.EffectivePort, Is.EqualTo(fake.CurrentPort), "Docker assigned a new port; metadata follows it.");
            Assert.That(registryAfter.Snapshot().Single().Port, Is.EqualTo(fake.CurrentPort));
            Assert.That(fake.AutoRestart, Has.Count.EqualTo(2));
        });
        await restarted.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task MissingContainerFallsBackToAFreshApply()
    {
        var (fake, registry, workspace, appId, targetId) = Setup();
        var first = Manager(fake, registry, workspace);
        await first.StartAsync(CancellationToken.None);
        await first.ApplyAsync(appId, targetId, RunnerRuntimeContainerManagerTests.Contract(fake.NextPort));
        await first.StopAsync(CancellationToken.None);
        fake.Forget("container-1");

        var restarted = Manager(fake, new RunnerRuntimeRegistry(), workspace);
        await restarted.StartAsync(CancellationToken.None);
        await WaitAsync(() => fake.CreateCalls == 2);
        await restarted.StopAsync(CancellationToken.None);

        Assert.That(fake.CreateCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task ContainerThatCannotBeStartedIsKeptAndReportedFailed()
    {
        var (fake, registry, workspace, appId, targetId) = Setup();
        var first = Manager(fake, registry, workspace);
        await first.StartAsync(CancellationToken.None);
        await first.ApplyAsync(appId, targetId, RunnerRuntimeContainerManagerTests.Contract(fake.NextPort));
        await first.StopAsync(CancellationToken.None);
        fake.Reboot();
        fake.StartFailure = new InvalidOperationException("port is already allocated");

        var failedRegistry = new RunnerRuntimeRegistry();
        var restarted = Manager(fake, failedRegistry, workspace);
        await restarted.StartAsync(CancellationToken.None);
        await WaitAsync(() => failedRegistry.PendingReports().Count == 1);
        await restarted.StopAsync(CancellationToken.None);

        var report = failedRegistry.PendingReports().Single();
        Assert.Multiple(() =>
        {
            Assert.That(report.Failed, Is.True);
            Assert.That(report.Error, Does.Contain("port is already allocated"));
            Assert.That(report.AppId, Is.EqualTo(appId));
            Assert.That(fake.Removed, Is.Empty, "A container that could not be started is never removed.");
            Assert.That(fake.CreateCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void RegistryReportsAFailureUntilTheRuntimeIsBackThenReportsRecoveryOnce()
    {
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        registry.MarkFailed(appId, targetId, "boom");
        registry.Acknowledge(registry.PendingReports());
        Assert.That(registry.PendingReports().Single().Failed, Is.True, "Sent again until the runtime is back.");

        registry.Register(appId, targetId, 19000);
        var recovered = registry.PendingReports();
        Assert.That(recovered.Single().Failed, Is.False);
        registry.Acknowledge(recovered);
        Assert.That(registry.PendingReports(), Is.Empty);
    }

    [Test]
    public void StoppingARuntimeDropsItsFailureReport()
    {
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        registry.MarkFailed(appId, targetId, "boom");
        registry.Remove(appId, targetId);
        Assert.That(registry.PendingReports(), Is.Empty);
    }

    private (RebootRuntime Fake, RunnerRuntimeRegistry Registry, RunnerWorkspaceService Workspace, Guid AppId, Guid TargetId) Setup()
        => (new RebootRuntime(), new RunnerRuntimeRegistry(), new RunnerWorkspaceService(), Guid.NewGuid(), Guid.NewGuid());

    private static RunnerRuntimeContainerManager Manager(RebootRuntime fake, RunnerRuntimeRegistry registry, RunnerWorkspaceService workspace)
        => new RunnerRuntimeContainerManager(workspace, registry, fake, NullLogger<RunnerRuntimeContainerManager>.Instance);

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.That(condition(), Is.True, "Timed out waiting for start-up recovery.");
    }

    /// <summary>A brokered runtime whose containers stay (stopped) across a simulated reboot, with a new host port each start.</summary>
    private sealed class RebootRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime, IScopedApplicationContainerRuntime
    {
        private readonly Dictionary<string, (bool Running, TcpListener? Listener)> containers = [];
        private readonly Dictionary<string, (Guid AppId, Guid TargetId)> owners = [];
        public int CreateCalls { get; private set; }
        public int CurrentPort { get; private set; }
        public List<string> Removed { get; } = [];
        public List<string> StartedExisting { get; } = [];
        public List<string> AutoRestart { get; } = [];
        public Exception? StartFailure { get; set; }

        public int NextPort => CurrentPort = FreePort();

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void Listen(string id)
        {
            containers[id].Listener?.Stop();
            var listener = new TcpListener(IPAddress.Loopback, CurrentPort);
            listener.Start();
            containers[id] = (true, listener);
        }

        public void Reboot()
        {
            foreach (var id in containers.Keys.ToArray())
            {
                containers[id].Listener?.Stop();
                containers[id] = (false, null);
            }
            CurrentPort = FreePort();
        }

        public void Forget(string id)
        {
            containers[id].Listener?.Stop();
            containers.Remove(id);
        }

        public Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            var id = "container-" + (++CreateCalls);
            containers[id] = (false, null);
            owners[id] = (request.AppId, request.DeploymentTargetId);
            return Task.FromResult(new DockerApplicationContainer(id, request.Name, new(request.Image, request.Image + "@sha256:" + new string('a', 64))));
        }

        public Task<ApplicationContainerStartResult> StartApplicationAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            if (StartFailure is not null) throw StartFailure;
            // The first start of a container the Runner just created is part of the deployment, not a recovery.
            if (!started.Add(id)) StartedExisting.Add(id);
            Listen(id);
            return Task.FromResult(new ApplicationContainerStartResult(true, "127.0.0.1", CurrentPort, 8080));
        }

        private readonly HashSet<string> started = [];

        public Task<DockerContainerIdentity> InspectAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            var exists = containers.TryGetValue(id, out var state);
            var labels = new Dictionary<string, string>();
            if (exists && owners.TryGetValue(id, out var owner))
            {
                labels["com.automaton.managed"] = "true";
                labels["com.automaton.app-id"] = owner.AppId.ToString();
                labels["com.automaton.deployment-target-id"] = owner.TargetId.ToString();
                labels["com.automaton.runtime-kind"] = "generic";
            }
            return Task.FromResult(new DockerContainerIdentity(id, exists, exists && state.Running, labels, "127.0.0.1", exists ? CurrentPort : null, 8080));
        }

        public async Task<bool> IsRunningAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
            => (await InspectAsync(scope, id, cancellationToken)).Running;

        public Task<string> GetLogsAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);

        public Task StopAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAndRemoveAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Removed.Add(id);
            Forget(id);
            return Task.CompletedTask;
        }

        public Task EnableAutoRestartAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            AutoRestart.Add(id);
            return Task.CompletedTask;
        }

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
