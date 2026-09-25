using System.Diagnostics;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class RunnerRuntimeProcessManagerDeploymentTests : RunnerRuntimeProcessManagerTestBase
{    [Test]
    public async Task StructuredRuntime_UsesArgumentsEnvironmentPortAndCapturesBothStreams()
    {
        await RunnerRuntimeProcessManagerStructuredRuntimeScenario.RunAsync();
    }

    [Test]
    public async Task FailedReplacement_LeavesHealthyRuntimeRoutedAndTracked()
    {
        await RunnerRuntimeProcessManagerFailedReplacementScenario.RunAsync();
    }

    [Test]
    public async Task FailedDeployment_LeavesCurrentArtifactFilesAndMetadataIntact()
    {
        await RunnerRuntimeProcessManagerFailedDeploymentScenario.RunAsync(root);
    }

    [Test]
    public async Task FailedBuild_LeavesCurrentRuntimeAndArtifactsIntact()
    {
        await RunnerRuntimeProcessManagerFailedBuildScenario.RunAsync(root);
    }

    [Test]
    public async Task InPlaceBuildWithoutOutputDirectory_PromotesPostBuildWorkspaceAsRuntimeArtifact()
    {
        await RunnerRuntimeProcessManagerInPlaceBuildScenario.RunAsync(root);
    }

    [Test]
    public async Task FinalMetadataCompactionFailure_RetainsPromotedRuntimeForRestartReconciliation()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var failFinalMetadataWrite = false;
        var manager = CreateManager(
            workspace,
            registry,
            metadata => failFinalMetadataWrite
                && metadata.Candidate is null
                && metadata.State == RuntimeReadinessState.Ready
                    ? new IOException("simulated final metadata failure")
                    : null);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "original"]));
        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var original = ReadMetadata(metadataPath);
        var originalArtifactRoot = original.ArtifactRoot!;

        failFinalMetadataWrite = true;
        var result = await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "replacement"]));
        var pending = ReadMetadata(metadataPath);
        var candidateArtifactRoot = pending.Candidate!.ArtifactRoot!;

        Assert.Multiple(() =>
        {
            Assert.That(result.PromotionIrreversible, Is.True);
            Assert.That(result.Warning, Is.Not.Null);
            Assert.That(IsProcessAlive(pending.Candidate.ProcessId!.Value), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            Assert.That(Directory.Exists(candidateArtifactRoot), Is.True);
            Assert.That(Directory.Exists(originalArtifactRoot), Is.False);
        });

        manager.Dispose();
        failFinalMetadataWrite = false;
        using var recoveryManager = CreateManager(workspace, registry);
        await recoveryManager.StartAsync(CancellationToken.None);

        var recovered = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(candidateArtifactRoot));
            Assert.That(IsProcessAlive(recovered.ProcessId!.Value), Is.True);
            Assert.That(Directory.Exists(candidateArtifactRoot), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await recoveryManager.StopAsync(appId, targetId);
    }

}
