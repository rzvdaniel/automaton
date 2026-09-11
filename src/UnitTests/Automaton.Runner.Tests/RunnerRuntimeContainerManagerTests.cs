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
    public async Task CandidateCleanupFailuresDoNotReplaceOriginalDeploymentFailure()
    {
        var fake = new FakeContainerRuntime { StartReady = false };
        var (manager, workspace, appId, targetId) = CreateManager(fake);
        await manager.StartAsync(CancellationToken.None);
        fake.GetLogsFailure = new InvalidOperationException("logs unavailable");
        fake.StopFailure = new InvalidOperationException("remove unavailable");

        var failure = Assert.ThrowsAsync<TimeoutException>(async () =>
            await manager.ApplyAsync(appId, targetId, Contract(19013)));

        Assert.That(failure!.Message, Does.Contain("did not become ready"));
        var retained = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(retained.Candidate, Is.Not.Null);
            Assert.That(retained.Candidate!.State, Is.EqualTo(RuntimeReadinessState.Failed));
            Assert.That(retained.Candidate.ContainerId, Is.EqualTo("container-1"));
        });
        fake.StopFailure = null;
        var replacement = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await replacement.StartAsync(CancellationToken.None);
        var reconciled = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.That(reconciled.Candidate, Is.Null);
        await replacement.StopAsync(CancellationToken.None);
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
    public async Task TransientInspectFailureKeepsReadyRuntimeRegistered()
    {
        var fake = new FakeContainerRuntime();
        var registry = new RunnerRuntimeRegistry();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var manager = CreateManager(fake, registry, workspace);
        await manager.StartAsync(CancellationToken.None);
        await manager.ApplyAsync(appId, targetId, Contract(19009));
        var port = fake.LastRequest!.HostPort;
        fake.InspectFailure = new InvalidOperationException("Docker daemon unavailable.");

        await Task.Delay(2500);

        var metadata = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(metadata.ContainerId, Is.EqualTo("container-1"));
            Assert.That(registry.IsPortRegistered(port), Is.True);
        });
        await manager.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartupRetriesTransientInspectFailureAndAdoptsExistingContainer()
    {
        var fake = new FakeContainerRuntime();
        var first = CreateManager(fake);
        await first.Manager.StartAsync(CancellationToken.None);
        await first.Manager.ApplyAsync(first.AppId, first.TargetId, Contract(19010));
        await first.Manager.StopAsync(CancellationToken.None);

        fake.InspectFailure = new InvalidOperationException("Docker daemon temporarily unavailable.");
        var registry = new RunnerRuntimeRegistry();
        var replacement = CreateManager(fake, registry, first.Workspace);
        await replacement.StartAsync(CancellationToken.None);
        fake.InspectFailure = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(250);
            var current = await ReadMetadataAsync(first.Workspace, first.AppId, first.TargetId);
            if (current.State == RuntimeReadinessState.Ready &&
                registry.IsPortRegistered(fake.LastRequest!.HostPort))
            {
                break;
            }
        }

        var metadata = await ReadMetadataAsync(first.Workspace, first.AppId, first.TargetId);
        Assert.Multiple(() =>
        {
            Assert.That(fake.StartCalls, Is.EqualTo(1));
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(metadata.ContainerId, Is.EqualTo("container-1"));
            Assert.That(registry.IsPortRegistered(fake.LastRequest!.HostPort), Is.True);
        });
        await replacement.StopAsync(first.AppId, first.TargetId);
    }

    [Test]
    public async Task LegacyCurrentWithReadyOciCandidatePromotesCandidateWithoutDockerizingLegacyProcess()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("candidate-1", appId, targetId, 19011);
        var metadata = LegacyMetadata(appId, targetId);
        metadata.Candidate = OciMetadata(appId, targetId, "candidate-1", 19011, RuntimeReadinessState.Ready);
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);

        var restored = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(restored.ContainerId, Is.EqualTo("candidate-1"));
            Assert.That(restored.Candidate, Is.Null);
            Assert.That(restored.ProcessId, Is.Null);
            Assert.That(fake.StartCalls, Is.Zero);
        });
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task LegacyCurrentWithStartingOciCandidateRemovesOwnedCandidateAndPreservesLegacyMetadata()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("candidate-1", appId, targetId, 19012);
        var metadata = LegacyMetadata(appId, targetId);
        metadata.Candidate = OciMetadata(appId, targetId, "candidate-1", 19012, RuntimeReadinessState.Starting);
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);

        var restored = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Candidate, Is.Null);
            Assert.That(restored.ProcessId, Is.EqualTo(1234));
            Assert.That(fake.Running["candidate-1"], Is.False);
            Assert.That(fake.StartCalls, Is.Zero);
        });
    }

    [Test]
    public async Task StartupClearsCandidateNameWhenNoContainerWasCreated()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var metadata = LegacyMetadata(appId, targetId);
        metadata.Candidate = OciMetadata(appId, targetId, null, 19014, RuntimeReadinessState.Starting);
        metadata.Candidate.ContainerName = "candidate-before-create";
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);

        var restored = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Candidate, Is.Null);
            Assert.That(restored.ProcessId, Is.EqualTo(1234));
            Assert.That(fake.StartCalls, Is.Zero);
        });
    }

    [Test]
    public async Task StartupRemovesStartingCandidateFoundByPersistedContainerName()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("candidate-before-id-persist", appId, targetId, 19015);
        var metadata = LegacyMetadata(appId, targetId);
        metadata.Candidate = OciMetadata(appId, targetId, null, 19015, RuntimeReadinessState.Starting);
        metadata.Candidate.ContainerName = "candidate-before-id-persist";
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);

        var restored = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Candidate, Is.Null);
            Assert.That(restored.ProcessId, Is.EqualTo(1234));
            Assert.That(fake.Running["candidate-before-id-persist"], Is.False);
        });
    }

    [Test]
    public async Task ExplicitStopRemovesVerifiedCurrentAndCandidateContainers()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("current", appId, targetId, 19016);
        fake.AddRunning("candidate", appId, targetId, 19017);
        var metadata = OciMetadata(appId, targetId, "current", 19016, RuntimeReadinessState.Ready);
        metadata.Candidate = OciMetadata(appId, targetId, "candidate", 19017, RuntimeReadinessState.Ready);
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StopAsync(appId, targetId);

        Assert.Multiple(() =>
        {
            Assert.That(fake.Running["current"], Is.False);
            Assert.That(fake.Running["candidate"], Is.False);
        });
    }

    [Test]
    public async Task ExplicitStopPreservesUnverifiedPersistedContainer()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("unverified", Guid.NewGuid(), targetId, 19018);
        await WriteMetadataAsync(workspace, OciMetadata(appId, targetId, "unverified", 19018, RuntimeReadinessState.Ready));

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => manager.StopAsync(appId, targetId));

        Assert.That(fake.Running["unverified"], Is.True);
        Assert.That(error!.Message, Does.Contain("incomplete"));
        await fake.StopAndRemoveAsync("unverified");
    }

    [Test]
    public async Task ExplicitStopFailurePreservesPersistedMetadataForRetry()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        fake.AddRunning("current", appId, targetId, 19019);
        await WriteMetadataAsync(workspace, OciMetadata(appId, targetId, "current", 19019, RuntimeReadinessState.Ready));
        fake.StopFailure = new InvalidOperationException("remove unavailable");

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        Assert.ThrowsAsync<InvalidOperationException>(() => manager.StopAsync(appId, targetId));

        Assert.Multiple(() =>
        {
            Assert.That(fake.Running["current"], Is.True);
            Assert.That(File.Exists(Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json")), Is.True);
        });
        fake.StopFailure = null;
        await fake.StopAndRemoveAsync("current");
    }

    [Test]
    public async Task HistoricalCurrentWithoutOciCandidateRemainsReadableWithoutRecreation()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var metadata = LegacyMetadata(appId, targetId);
        await WriteMetadataAsync(workspace, metadata);

        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);

        var restored = await ReadMetadataAsync(workspace, appId, targetId);
        Assert.Multiple(() =>
        {
            Assert.That(restored.ProcessId, Is.EqualTo(1234));
            Assert.That(restored.ContainerId, Is.Null);
            Assert.That(fake.StartCalls, Is.Zero);
        });
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

    [Test]
    public async Task HostedShutdownLeavesContainerForReplacementManagerToAdopt()
    {
        var fake = new FakeContainerRuntime();
        var registry = new RunnerRuntimeRegistry();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var manager = CreateManager(fake, registry, workspace);
        await manager.StartAsync(CancellationToken.None);
        await manager.ApplyAsync(appId, targetId, Contract(19006));

        await manager.StopAsync(CancellationToken.None);

        Assert.That(fake.Running["container-1"], Is.True);
        var replacementRegistry = new RunnerRuntimeRegistry();
        var replacement = CreateManager(fake, replacementRegistry, workspace);
        await replacement.StartAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fake.StartCalls, Is.EqualTo(1));
            Assert.That(replacementRegistry.IsPortRegistered(fake.LastRequest!.HostPort), Is.True);
        });

        await replacement.StopAsync(appId, targetId);
        Assert.That(fake.Running["container-1"], Is.False);
    }

    [Test]
    public async Task ReadyCandidateRecoveryDoesNotRemovePreviousContainerWithMismatchedLabels()
    {
        var fake = new FakeContainerRuntime();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var manager = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await manager.StartAsync(CancellationToken.None);
        await manager.ApplyAsync(appId, targetId, Contract(19007));

        var metadata = await ReadMetadataAsync(workspace, appId, targetId);
        fake.AddRunning("container-2", appId, targetId, 19008);
        var candidate = Clone(metadata);
        candidate.ContainerId = "container-2";
        candidate.ContainerName = "candidate";
        candidate.EffectivePort = 19008;
        candidate.State = RuntimeReadinessState.Ready;
        candidate.ArtifactRoot = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, Guid.NewGuid());
        Directory.CreateDirectory(candidate.ArtifactRoot);
        metadata.Candidate = candidate;
        metadata.State = RuntimeReadinessState.Starting;
        fake.OverrideIdentity("container-1", Guid.NewGuid(), targetId);
        await WriteMetadataAsync(workspace, metadata);

        var replacement = CreateManager(fake, new RunnerRuntimeRegistry(), workspace);
        await replacement.StartAsync(CancellationToken.None);

        Assert.That(fake.StoppedContainers, Does.Not.Contain("container-1"));
        Assert.That(fake.Running["container-1"], Is.True);
        Assert.That((await ReadMetadataAsync(workspace, appId, targetId)).ContainerId, Is.EqualTo("container-2"));
        await replacement.StopAsync(appId, targetId);
        await fake.StopAndRemoveAsync("container-1");
    }

    [Test]
    public async Task BrokeredStartPortIsPersistedBeforeReadinessAndRecoveredOnRestart()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var assignedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var runtime = new ScopedContainerRuntime(assignedPort);
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var manager = new RunnerRuntimeContainerManager(workspace, registry, runtime, NullLogger<RunnerRuntimeContainerManager>.Instance);
        await manager.StartAsync(CancellationToken.None);

        var apply = manager.ApplyAsync(appId, targetId, Contract(8080), candidateId: candidateId);
        RunnerRuntimeDeploymentMetadata? pending = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(25);
            pending = await ReadMetadataAsync(workspace, appId, targetId);
            if (pending.Candidate?.EffectivePort == assignedPort)
                break;
        }
        Assert.That(pending!.Candidate!.EffectivePort, Is.EqualTo(assignedPort));
        runtime.EnableReadiness();
        await apply;
        var ready = await ReadMetadataAsync(workspace, appId, targetId);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Created!.HostPort, Is.Zero);
            Assert.That(runtime.Created.CandidateId, Is.EqualTo(candidateId));
            Assert.That(ready.CandidateId, Is.EqualTo(candidateId));
            Assert.That(runtime.Scopes, Is.All.EqualTo(new ApplicationContainerScope(appId, targetId)));
            Assert.That(registry.IsPortRegistered(assignedPort), Is.True);
        });
        await manager.StopAsync(CancellationToken.None);

        runtime.InspectedPort = assignedPort + 1;
        var replacementRegistry = new RunnerRuntimeRegistry();
        var replacement = new RunnerRuntimeContainerManager(workspace, replacementRegistry, runtime, NullLogger<RunnerRuntimeContainerManager>.Instance);
        await replacement.StartAsync(CancellationToken.None);
        RunnerRuntimeDeploymentMetadata recovered = await ReadMetadataAsync(workspace, appId, targetId);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            recovered = await ReadMetadataAsync(workspace, appId, targetId);
            if (recovered.ContainerId == "brokered-container"
                && recovered.EffectivePort == assignedPort + 1
                && replacementRegistry.IsPortRegistered(assignedPort + 1))
                break;
            await Task.Delay(100);
        }
        Assert.Multiple(() =>
        {
            Assert.That(recovered.ContainerId, Is.EqualTo("brokered-container"));
            Assert.That(recovered.EffectivePort, Is.EqualTo(assignedPort + 1));
            Assert.That(replacementRegistry.IsPortRegistered(assignedPort + 1), Is.True);
            Assert.That(runtime.CreateCalls, Is.EqualTo(1));
        });
        await replacement.StopAsync(appId, targetId);
    }

    [Test]
    public async Task BrokeredStartupReturnsBeforeReconciliationInspection()
    {
        var runtime = new ScopedContainerRuntime(19080) { InspectGate = new TaskCompletionSource<bool>() };
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        await WriteMetadataAsync(workspace, new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(), DeploymentTargetId = targetId.ToString(),
            ContainerId = "existing", ContainerName = "existing", State = RuntimeReadinessState.Ready,
            Runtime = Contract(19080).Runtime, Health = Contract(19080).Health, Storage = Contract(19080).Storage
        });
        var manager = new RunnerRuntimeContainerManager(workspace, new RunnerRuntimeRegistry(), runtime, NullLogger<RunnerRuntimeContainerManager>.Instance);
        var startup = manager.StartAsync(CancellationToken.None);
        Assert.That(await Task.WhenAny(startup, Task.Delay(500)), Is.SameAs(startup));
        Assert.That(runtime.Scopes, Is.Empty);
        runtime.InspectGate.SetResult(true);
        await manager.StopAsync(CancellationToken.None);
    }

    private (RunnerRuntimeContainerManager Manager, RunnerWorkspaceService Workspace, Guid AppId, Guid TargetId) CreateManager(FakeContainerRuntime fake)
    {
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        return (CreateManager(fake, new RunnerRuntimeRegistry(), workspace), workspace, appId, targetId);
    }

    private RunnerRuntimeContainerManager CreateManager(
        FakeContainerRuntime fake,
        RunnerRuntimeRegistry? registry = null,
        RunnerWorkspaceService? workspace = null)
    {
        return new RunnerRuntimeContainerManager(
            workspace ?? new RunnerWorkspaceService(),
            registry ?? new RunnerRuntimeRegistry(),
            fake,
            NullLogger<RunnerRuntimeContainerManager>.Instance);
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

    private static RunnerRuntimeDeploymentMetadata LegacyMetadata(Guid appId, Guid targetId) => new()
    {
        AppId = appId.ToString(),
        DeploymentTargetId = targetId.ToString(),
        Runtime = new ApplicationRuntimeSpec
        {
            Port = 19000,
            Command = new ApplicationCommandSpec { Executable = "legacy-server" }
        },
        Health = new ApplicationHealthSpec { StartupTimeoutSeconds = 1 },
        Storage = new ApplicationStorageSpec(),
        ProcessId = 1234,
        State = RuntimeReadinessState.Ready
    };

    private static RunnerRuntimeDeploymentMetadata OciMetadata(
        Guid appId,
        Guid targetId,
        string? containerId,
        int port,
        string state) => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "runtime:1",
                Port = port,
                Command = new ApplicationCommandSpec { Executable = "server" }
            },
            Health = new ApplicationHealthSpec { StartupTimeoutSeconds = 1 },
            Storage = new ApplicationStorageSpec(),
            ContainerId = containerId,
            ContainerName = containerId,
            EffectivePort = port,
            State = state
        };

    private static async Task<RunnerRuntimeDeploymentMetadata> ReadMetadataAsync(RunnerWorkspaceService workspace, Guid appId, Guid targetId)
        => System.Text.Json.JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private static async Task WriteMetadataAsync(RunnerWorkspaceService workspace, RunnerRuntimeDeploymentMetadata metadata)
    {
        var path = Path.Combine(workspace.GetAppAutomationRoot(Guid.Parse(metadata.AppId), Guid.Parse(metadata.DeploymentTargetId)), "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(
            metadata,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }

    private static RunnerRuntimeDeploymentMetadata Clone(RunnerRuntimeDeploymentMetadata metadata)
        => System.Text.Json.JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private sealed class ScopedContainerRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime, IScopedApplicationContainerRuntime
    {
        private readonly int assignedPort;
        private TcpListener? listener;
        public int InspectedPort { get; set; }
        public TaskCompletionSource<bool>? InspectGate { get; set; }
        public int CreateCalls { get; private set; }
        public DockerApplicationContainerRequest? Created { get; private set; }
        public List<ApplicationContainerScope> Scopes { get; } = [];
        private bool exists;
        private bool running;

        public ScopedContainerRuntime(int assignedPort)
            => this.assignedPort = InspectedPort = assignedPort;

        public void EnableReadiness()
        {
            listener = new TcpListener(IPAddress.Loopback, assignedPort);
            listener.Start();
        }

        public Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            Created = request;
            exists = true;
            return Task.FromResult(new DockerApplicationContainer("brokered-container", "brokered-name", new(request.Image, request.Image + "@sha256:" + new string('a', 64))));
        }

        public Task<ApplicationContainerStartResult> StartApplicationAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            running = true;
            return Task.FromResult(new ApplicationContainerStartResult(true, "127.0.0.1", assignedPort, 8080));
        }

        public Task<DockerContainerIdentity> InspectAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return InspectCoreAsync(scope, id, cancellationToken);
        }

        private async Task<DockerContainerIdentity> InspectCoreAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken)
        {
            if (InspectGate is not null) await InspectGate.Task.WaitAsync(cancellationToken);
            return new DockerContainerIdentity(id, exists, running, Labels(scope), "127.0.0.1", InspectedPort, 8080);
        }

        public async Task<bool> IsRunningAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
            => (await InspectAsync(scope, id, cancellationToken)).Running;

        public Task<string> GetLogsAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(string.Empty);
        }

        public Task StopAndRemoveAsync(ApplicationContainerScope scope, string id, CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            exists = running = false;
            listener?.Stop();
            return Task.CompletedTask;
        }

        private static IReadOnlyDictionary<string, string> Labels(ApplicationContainerScope scope) => new Dictionary<string, string>
        {
            ["com.automaton.managed"] = "true",
            ["com.automaton.app-id"] = scope.AppId.ToString(),
            ["com.automaton.deployment-target-id"] = scope.DeploymentTargetId.ToString(),
            ["com.automaton.runtime-kind"] = "generic"
        };

        public Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartApplicationAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAndRemoveAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsRunningAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DockerContainerIdentity> InspectAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetLogsAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeContainerRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime
    {
        public bool StartReady { get; set; } = true;
        public Dictionary<string, bool> Running { get; } = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Guid AppId, Guid TargetId)> identities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DockerApplicationContainerRequest> requests = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TcpListener> listeners = new(StringComparer.Ordinal);
        private int sequence;
        public int StartCalls { get; private set; }
        public DockerApplicationContainerRequest? LastRequest { get; private set; }
        public List<string> StoppedContainers { get; } = [];
        public Exception? InspectFailure { get; set; }
        public Exception? GetLogsFailure { get; set; }
        public Exception? StopFailure { get; set; }

        public Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default)
            => Task.FromResult(new DockerImageIdentity(image, image + "@sha256:" + new string('a', 64)));

        public Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            LastRequest = request;
            var id = "container-" + (++sequence);
            Running[id] = false;
            identities[id] = (request.AppId, request.DeploymentTargetId);
            requests[id] = request;
            return Task.FromResult(new DockerApplicationContainer(id, request.Name, new DockerImageIdentity(request.Image, request.Image + "@sha256:" + new string('a', 64))));
        }

        public Task StartApplicationAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        {
            Running[containerIdOrName] = true;
            if (StartReady && requests.TryGetValue(containerIdOrName, out var request))
            {
                var listener = new TcpListener(IPAddress.Loopback, request.HostPort);
                listener.Start();
                listeners[containerIdOrName] = listener;
            }
            return Task.CompletedTask;
        }

        public async Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default)
        {
            var created = await CreateApplicationAsync(request, cancellationToken);
            await StartApplicationAsync(created.Id, cancellationToken);
            return created;
        }

        public Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        {
            if (StopFailure is not null) throw StopFailure;
            StoppedContainers.Add(containerIdOrName);
            Running[containerIdOrName] = false;
            identities.Remove(containerIdOrName);
            requests.Remove(containerIdOrName);
            if (listeners.Remove(containerIdOrName, out var listener)) listener.Stop();
            return Task.CompletedTask;
        }

        public void AddRunning(string id, Guid appId, Guid targetId, int port)
        {
            Running[id] = true;
            identities[id] = (appId, targetId);
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listeners[id] = listener;
        }

        public void OverrideIdentity(string id, Guid appId, Guid targetId)
            => identities[id] = (appId, targetId);

        public Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
            => Task.FromResult(Running.TryGetValue(containerIdOrName, out var running) && running);

        public Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        {
            if (InspectFailure is not null) throw InspectFailure;
            return Task.FromResult(CreateIdentity(containerIdOrName));
        }

        private DockerContainerIdentity CreateIdentity(string containerIdOrName)
        {
            var exists = Running.ContainsKey(containerIdOrName);
            var labels = new Dictionary<string, string>();
            if (identities.TryGetValue(containerIdOrName, out var identity))
            {
                labels["com.automaton.managed"] = "true";
                labels["com.automaton.app-id"] = identity.AppId.ToString();
                labels["com.automaton.deployment-target-id"] = identity.TargetId.ToString();
                labels["com.automaton.runtime-kind"] = "generic";
            }
            return new DockerContainerIdentity(containerIdOrName, exists, exists && Running[containerIdOrName], labels);
        }

        public Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        {
            if (GetLogsFailure is not null) throw GetLogsFailure;
            return Task.FromResult(string.Empty);
        }
    }
}
