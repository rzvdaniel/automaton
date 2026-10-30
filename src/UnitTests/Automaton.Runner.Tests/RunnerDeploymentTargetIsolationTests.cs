using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public class RunnerDeploymentTargetIsolationTests
{
    private string root = null!;
    private string? originalRoot;
    private string? originalScriptsDir;
    private string? originalProfile;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsDir = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        originalProfile = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE");
        root = Path.Combine(
            Path.GetTempPath(),
            "automaton-target-deployment-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", "MachineManagement");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsDir);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", originalProfile);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task DeployingTwoTargetsOfOneApp_PreservesIndependentRuntimeContent()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = CreateDeploymentService(workspace, processManager);
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();
        var firstPackage = CreatePackage(appId, "first target");
        var secondPackage = CreatePackage(appId, "second target");

        var first = await service.DeployPackage(RunnerDeploymentFixtureBuildExecutor.Request(appId, firstTargetId, firstPackage));
        var second = await service.DeployPackage(RunnerDeploymentFixtureBuildExecutor.Request(appId, secondTargetId, secondPackage));

        var firstContent = await File.ReadAllTextAsync(Path.Combine(
            workspace.GetAppRuntimeApplicationDirectory(appId, firstTargetId),
            "index.html"));
        var secondContent = await File.ReadAllTextAsync(Path.Combine(
            workspace.GetAppRuntimeApplicationDirectory(appId, secondTargetId),
            "index.html"));
        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True, first.Message);
            Assert.That(second.Success, Is.True, second.Message);
            Assert.That(first.DeploymentTargetId, Is.EqualTo(firstTargetId));
            Assert.That(second.DeploymentTargetId, Is.EqualTo(secondTargetId));
            Assert.That(firstContent, Is.EqualTo("first target"));
            Assert.That(secondContent, Is.EqualTo("second target"));
            Assert.That(processManager.AppliedTargets, Does.Contain(firstTargetId));
            Assert.That(processManager.AppliedTargets, Does.Contain(secondTargetId));
            Assert.That(processManager.StoppedTargets, Does.Contain(firstTargetId));
            Assert.That(processManager.StoppedTargets, Does.Contain(secondTargetId));
        });
    }

    [Test]
    public async Task CleanupTargetAsync_IsTargetScopedAndIdempotent()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var deletedTargetId = Guid.NewGuid();
        var preservedTargetId = Guid.NewGuid();
        Directory.CreateDirectory(workspace.GetDeploymentTargetRoot(appId, deletedTargetId));
        Directory.CreateDirectory(workspace.GetDeploymentTargetRoot(appId, preservedTargetId));

        await service.CleanupTargetAsync(appId, deletedTargetId);
        await service.CleanupTargetAsync(appId, deletedTargetId);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(workspace.GetDeploymentTargetRoot(appId, deletedTargetId)),
                Is.False);
            Assert.That(
                Directory.Exists(workspace.GetDeploymentTargetRoot(appId, preservedTargetId)),
                Is.True);
            Assert.That(
                processManager.StoppedTargets.Count(target => target == deletedTargetId),
                Is.EqualTo(2));
        });
    }

    [Test]
    public void CleanupTargetAsync_WhenRuntimeCleanupFailsPreservesTargetMetadata()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager
        {
            StopFailure = new InvalidOperationException("runtime cleanup unavailable")
        };
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var targetRoot = workspace.GetDeploymentTargetRoot(appId, targetId);
        Directory.CreateDirectory(targetRoot);
        var metadataPath = Path.Combine(targetRoot, "automaton", "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        File.WriteAllText(metadataPath, "managed container identity");

        Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupTargetAsync(appId, targetId));

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(targetRoot), Is.True);
            Assert.That(File.Exists(metadataPath), Is.True);
            Assert.That(File.ReadAllText(metadataPath), Is.EqualTo("managed container identity"));
        });
    }

    [Test]
    public async Task CleanupTargetAsync_WithCustomScriptsRoot_RemovesBothTargetTreesAndPreservesBothDataRoots()
    {
        var scriptsRoot = Path.Combine(root, "legacy-controller-files");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", null);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", scriptsRoot);
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var canonicalTarget = workspace.GetDeploymentTargetRoot(appId, targetId);
        var legacyBackend = workspace.GetLegacyControllerBackendDirectory(appId, targetId);
        var legacyVenv = workspace.GetLegacyControllerVenvDirectory(appId, targetId);
        var legacyData = workspace.GetLegacyControllerDataDirectory(appId);
        var canonicalData = workspace.GetAppDataDirectory(appId);
        Directory.CreateDirectory(canonicalTarget);
        Directory.CreateDirectory(legacyBackend);
        Directory.CreateDirectory(legacyVenv);
        Directory.CreateDirectory(legacyData);
        Directory.CreateDirectory(canonicalData);
        File.WriteAllText(Path.Combine(canonicalTarget, "deployment.json"), "target");
        File.WriteAllText(Path.Combine(legacyBackend, "todos.py"), "controller");
        File.WriteAllText(Path.Combine(legacyVenv, "marker.txt"), "venv");
        File.WriteAllText(Path.Combine(legacyData, "user.db"), "legacy durable");
        File.WriteAllText(Path.Combine(canonicalData, "user.db"), "generic durable");

        await service.CleanupTargetAsync(appId, targetId);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(canonicalTarget), Is.False);
            Assert.That(Directory.Exists(Path.Combine(scriptsRoot, appId.ToString(), "targets", targetId.ToString())), Is.False);
            Assert.That(File.Exists(Path.Combine(legacyBackend, "todos.py")), Is.False);
            Assert.That(File.Exists(Path.Combine(legacyVenv, "marker.txt")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(legacyData, "user.db")), Is.EqualTo("legacy durable"));
            Assert.That(File.ReadAllText(Path.Combine(canonicalData, "user.db")), Is.EqualTo("generic durable"));
        });
    }

    [Test]
    public async Task DeployingPackage_UsesGenericRuntimeContractAndRequestsProcessStart()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = CreateDeploymentService(workspace, processManager);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var package = CreatePackage(appId, "runtime target", hasRuntime: true);

        var result = await service.DeployPackage(RunnerDeploymentFixtureBuildExecutor.Request(appId, targetId, package));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(processManager.StartedTargets, Does.Contain(targetId));
        Assert.That(processManager.StartedContracts[targetId].Runtime.Command!.Executable, Is.EqualTo("dotnet"));
        Assert.That(processManager.CandidateIds[targetId], Is.Not.EqualTo(Guid.Empty));
        Assert.That(processManager.ArtifactRoots[targetId], Does.Contain(processManager.CandidateIds[targetId].ToString()));
    }

    [Test]
    public async Task SupersededExecutionAttempt_CannotReachRuntimePromotion()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = CreateDeploymentService(workspace, processManager);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();
        var firstPackage = CreatePackage(
            appId,
            "first",
            hasRuntime: true,
            build: new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [RunnerDeploymentFixtureBuildExecutor.FixturePath(), "--delay", "1500"]
                }
            });
        var secondPackage = CreatePackage(appId, "second", hasRuntime: true);

        var firstTask = service.DeployPackage(RunnerDeploymentFixtureBuildExecutor.Request(appId, targetId, firstPackage, firstAttemptId));
        await Task.Delay(150);
        var secondTask = service.DeployPackage(RunnerDeploymentFixtureBuildExecutor.Request(appId, targetId, secondPackage, secondAttemptId));

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Multiple(() =>
        {
            Assert.That(results[0].Success, Is.False);
            Assert.That(results[0].Message, Does.Contain("canceled").IgnoreCase);
            Assert.That(results[1].Success, Is.True, results[1].Message);
            Assert.That(processManager.AppliedTargets.Count(target => target == targetId), Is.EqualTo(1));
        });
    }

    private string CreatePackage(
        Guid appId,
        string frontendContent,
        bool hasRuntime = false,
        ApplicationBuildSpec? build = null,
        bool includeAction = false)
    {
        var packagePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(root);
        var application = hasRuntime
            ? new ApplicationContract
            {
                Runtime = new ApplicationRuntimeSpec
                {
                    Image = "test/runtime:1",
                    Command = new ApplicationCommandSpec { Executable = "dotnet" }
                }
            }
            : ApplicationContractDefaults.Create();
        application.Build = build ?? application.Build;
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "manifest.json",
            JsonSerializer.Serialize(new AppDeploymentPackageManifest
            {
                AppId = appId.ToString(),
                Application = application
            }));
        WriteEntry(archive, "runtime/application/index.html", frontendContent);
        WriteEntry(archive, "source/placeholder.txt", "source");
        if (includeAction)
        {
            WriteEntry(archive, "source/actions/dependency-hook/action.yaml", """
                apiVersion: automaton.dev/v1
                kind: Action
                metadata:
                  key: dependency-hook
                spec:
                  runtime: python
                  entrypoint: main.py
                """);
            WriteEntry(archive, "source/actions/dependency-hook/main.py", "print('action')");
            WriteEntry(archive, "source/actions/dependency-hook/requirements.txt", "hook-package==1.0");
        }
        return packagePath;
    }

    private static RunnerDeploymentService CreateDeploymentService(
        RunnerWorkspaceService workspace,
        StubBackendProcessManager processManager)
        => new(workspace, processManager, new AppActionDeploymentService(workspace), new RunnerDeploymentFixtureBuildExecutor());

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class StubBackendProcessManager : IAppRuntimeProcessManager
    {
        public List<Guid> StartedTargets { get; } = [];
        public List<Guid> StoppedTargets { get; } = [];
        public List<Guid> AppliedTargets { get; } = [];
        public Dictionary<Guid, ApplicationContract> StartedContracts { get; } = [];
        public Dictionary<Guid, Guid> CandidateIds { get; } = [];
        public Dictionary<Guid, string> ArtifactRoots { get; } = [];
        public Exception? StopFailure { get; set; }

        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null,
            Guid? candidateId = null,
            Guid? deploymentAttemptId = null)
        {
            StartedTargets.Add(deploymentTargetId);
            AppliedTargets.Add(deploymentTargetId);
            StartedContracts[deploymentTargetId] = contract;
            if (candidateId.HasValue) CandidateIds[deploymentTargetId] = candidateId.Value;
            if (artifactRoot is not null) ArtifactRoots[deploymentTargetId] = artifactRoot;
            if (contract.Runtime.Command is null)
                StoppedTargets.Add(deploymentTargetId);
            return Task.FromResult(new RuntimeApplyResult
            {
                PromotionIrreversible = contract.Runtime.Command is not null
            });
        }

        public Task StopAsync(Guid appId, Guid deploymentTargetId)
        {
            if (StopFailure is not null) throw StopFailure;
            StoppedTargets.Add(deploymentTargetId);
            return Task.CompletedTask;
        }
    }

}
