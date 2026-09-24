using System.Diagnostics;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunnerRuntimeProcessManagerTests : RunnerRuntimeProcessManagerTestBase
{
    [Test]
    public async Task StopWithCancelledShutdownToken_KillsTheEntireProcessTree()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await manager.ApplyAsync(appId, targetId, CreateContract(["--spawn-child"]));
        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var metadata = ReadMetadata(metadataPath);
        var outputPath = Path.Combine(
            workspace.GetAppRuntimeLogsDirectory(appId, targetId, metadata.ArtifactId!),
            "runtime.stdout.log");
        var output = await WaitForLogLineAsync(outputPath, "child:");
        var childMarker = output.LastIndexOf("child:", StringComparison.Ordinal);
        var childPid = int.Parse(output[(childMarker + "child:".Length)..].Split('\r', '\n')[0]);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await manager.StopAsync(cancelled.Token);

        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(metadata.ProcessId!.Value), Is.False);
            Assert.That(IsProcessAlive(childPid), Is.False);
        });
    }

    [Test]
    public async Task RecoveryWithPersistedCandidate_FencesBothProcessesAndRebuildsDesiredRuntime()
    {
        await RunnerRuntimeProcessManagerRecoveryScenarios.PersistedCandidateIsRebuiltAsync();
    }

    [Test]
    public async Task RecoveryWithFirstDeploymentCandidate_ReusesCandidateWithoutCurrentRuntime()
    {
        await RunnerRuntimeProcessManagerRecoveryScenarios.FirstDeploymentCandidateIsReusedAsync();
    }

    [Test]
    public void RunnerBoundary_RejectsAbsoluteAndTraversalPaths()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var cases = new[] { "../outside", "bin/../../outside", "/usr/bin/tool", "C:\\tools\\tool.exe" };

        foreach (var executable in cases)
        {
            var contract = CreateContract(["--tcp"]);
            contract.Runtime.Command!.Executable = executable;
            Assert.That(
                async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
                Throws.TypeOf<InvalidOperationException>(),
                executable);
        }
    }

    [Test]
    public void ArtifactRootOutsideManagedTarget_IsRejectedBeforeExecution()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(workspace, registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var outsideRoot = Path.Combine(root, "outside-artifacts");

        Assert.That(
            async () => await manager.ApplyAsync(
                appId,
                targetId,
                CreateContract(["--tcp"]),
                artifactRoot: outsideRoot),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(Directory.Exists(outsideRoot), Is.False);
    }

    [Test]
    public void StartupFailureIsDeterministic()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var contract = CreateContract(["--does-not-listen"]);
        contract.Runtime.Command!.Arguments = ["--version"];
        contract.Health.StartupTimeoutSeconds = 1;

        Assert.That(
            async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
            Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public async Task Recovery_UsesGenericMetadataWithoutFrameworkFields()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--tcp"]);
        using (var firstManager = CreateManager(workspace, registry))
            await firstManager.ApplyAsync(appId, targetId, contract);

        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var metadataText = await File.ReadAllTextAsync(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            Assert.That(metadataText, Does.Contain("effectivePort"));
            Assert.That(metadataText, Does.Contain("processId"));
            Assert.That(metadataText, Does.Not.Contain("hasBackend"));
            Assert.That(metadataText, Does.Not.Contain("backendModule"));
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

}
