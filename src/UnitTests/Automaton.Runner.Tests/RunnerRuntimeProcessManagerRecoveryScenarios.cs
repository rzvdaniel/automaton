using System.Diagnostics;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerRecoveryScenarios
{
    public static async Task PersistedCandidateIsRebuiltAsync()
    {
        var scenario = await CreatePersistedCandidateScenarioAsync();
        using var manager = CreateManager(scenario.Workspace, scenario.Registry);
        await manager.StartAsync(CancellationToken.None);
        AssertPersistedCandidateWasFenced(scenario);
        await manager.StopAsync(scenario.AppId, scenario.TargetId);
    }

    public static async Task FirstDeploymentCandidateIsReusedAsync()
    {
        var scenario = await CreateFirstDeploymentCandidateScenarioAsync();
        using var manager = CreateManager(scenario.Workspace, scenario.Registry);
        await manager.StartAsync(CancellationToken.None);
        AssertFirstDeploymentCandidateWasRebuilt(scenario);
        await manager.StopAsync(scenario.AppId, scenario.TargetId);
    }

    private static async Task<RecoveryScenario> CreatePersistedCandidateScenarioAsync()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var currentRoot = CreateCandidateRoot(workspace, appId, targetId);
        var candidateRoot = CreateCandidateRoot(workspace, appId, targetId);
        var currentPort = 30_000 + Random.Shared.Next(500);
        var candidatePort = currentPort + 1;
        using var currentProcess = StartFixtureProcess(currentRoot, currentPort, "current");
        using var candidateProcess = StartFixtureProcess(candidateRoot, candidatePort, "candidate");
        await Task.Delay(250);

        var currentContract = CreateContract(["--tcp", "current"]);
        var candidateContract = CreateContract(["--tcp", "candidate"]);
        var current = CreateRecord(appId, targetId, currentContract, currentRoot, currentPort, currentProcess, "current", RuntimeReadinessState.Ready);
        var candidate = CreateRecord(appId, targetId, candidateContract, candidateRoot, candidatePort, candidateProcess, "candidate", RuntimeReadinessState.Starting);
        var metadata = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = current.Runtime,
            Health = current.Health,
            EffectivePort = current.EffectivePort,
            ProcessId = current.ProcessId,
            ProcessStartTimeUtc = current.ProcessStartTimeUtc,
            ArtifactRoot = current.ArtifactRoot,
            ArtifactId = current.ArtifactId,
            State = RuntimeReadinessState.Ready,
            Candidate = candidate
        };
        var metadataPath = await WriteMetadataAsync(workspace, appId, targetId, metadata);
        return new RecoveryScenario(workspace, registry, appId, targetId, metadataPath, candidateRoot, currentProcess.Id, candidateProcess.Id);
    }

    private static async Task<RecoveryScenario> CreateFirstDeploymentCandidateScenarioAsync()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidateRoot = CreateCandidateRoot(workspace, appId, targetId);
        var candidateContract = CreateContract(["--tcp", "first-deployment"]);
        var candidatePort = 30_000 + Random.Shared.Next(500);
        candidateContract.Runtime.Port = candidatePort;
        using var candidateProcess = StartFixtureProcess(candidateRoot, candidatePort, "first-deployment");
        await Task.Delay(250);

        var candidate = CreateRecord(
            appId,
            targetId,
            candidateContract,
            candidateRoot,
            candidatePort,
            candidateProcess,
            "candidate",
            RuntimeReadinessState.Starting);
        var metadata = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            State = RuntimeReadinessState.Stopped,
            Candidate = candidate
        };
        var metadataPath = await WriteMetadataAsync(workspace, appId, targetId, metadata);
        return new RecoveryScenario(workspace, registry, appId, targetId, metadataPath, candidateRoot, null, candidateProcess.Id);
    }

    private static string CreateCandidateRoot(RunnerWorkspaceService workspace, Guid appId, Guid targetId)
    {
        var root = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }

    private static RunnerRuntimeDeploymentMetadata CreateRecord(
        Guid appId,
        Guid targetId,
        ApplicationContract contract,
        string artifactRoot,
        int port,
        Process process,
        string artifactId,
        string state)
        => new()
        {
            AppId = appId.ToString(),
            DeploymentTargetId = targetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            EffectivePort = port,
            ProcessId = process.Id,
            ProcessStartTimeUtc = process.StartTime.ToUniversalTime(),
            ArtifactRoot = artifactRoot,
            ArtifactId = artifactId,
            State = state
        };

    private static async Task<string> WriteMetadataAsync(
        RunnerWorkspaceService workspace,
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata metadata)
    {
        var path = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(metadata));
        return path;
    }

    private static void AssertPersistedCandidateWasFenced(RecoveryScenario scenario)
    {
        var recovered = ReadMetadata(scenario.MetadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(scenario.CurrentProcessId!.Value), Is.False);
            Assert.That(IsProcessAlive(scenario.CandidateProcessId), Is.False);
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(scenario.CandidateRoot));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(scenario.CurrentProcessId));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(scenario.CandidateProcessId));
            Assert.That(scenario.Registry.TryGet(scenario.AppId, scenario.TargetId, out _), Is.True);
        });
    }

    private static void AssertFirstDeploymentCandidateWasRebuilt(RecoveryScenario scenario)
    {
        var recovered = ReadMetadata(scenario.MetadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(scenario.CandidateProcessId), Is.False);
            Assert.That(recovered.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(recovered.Candidate, Is.Null);
            Assert.That(recovered.ArtifactRoot, Is.EqualTo(scenario.CandidateRoot));
            Assert.That(recovered.ProcessId, Is.Not.EqualTo(scenario.CandidateProcessId));
            Assert.That(IsProcessAlive(recovered.ProcessId!.Value), Is.True);
            Assert.That(Directory.Exists(scenario.CandidateRoot), Is.True);
            Assert.That(scenario.Registry.TryGet(scenario.AppId, scenario.TargetId, out _), Is.True);
        });
    }

    private sealed record RecoveryScenario(
        RunnerWorkspaceService Workspace,
        RunnerRuntimeRegistry Registry,
        Guid AppId,
        Guid TargetId,
        string MetadataPath,
        string CandidateRoot,
        int? CurrentProcessId,
        int CandidateProcessId);
}
