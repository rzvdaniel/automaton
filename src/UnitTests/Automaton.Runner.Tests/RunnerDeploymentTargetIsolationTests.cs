using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerDeploymentTargetIsolationTests
{
    private string root = null!;
    private string? originalRoot;
    private string? originalScriptsDir;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsDir = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        root = Path.Combine(
            Path.GetTempPath(),
            "automaton-target-deployment-tests",
            Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsDir);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task DeployingTwoTargetsOfOneApp_PreservesIndependentRuntimeContent()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();
        var firstPackage = CreatePackage(appId, "first target");
        var secondPackage = CreatePackage(appId, "second target");

        var first = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = firstTargetId,
            PackagePath = firstPackage,
            Clean = true
        });
        var second = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = secondTargetId,
            PackagePath = secondPackage,
            Clean = true
        });

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
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var package = CreatePackage(appId, "runtime target", hasRuntime: true);

        var result = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = package,
            Clean = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(processManager.StartedTargets, Does.Contain(targetId));
            Assert.That(processManager.StartedContracts[targetId].Runtime.Command!.Executable, Is.EqualTo("dotnet"));
        });
    }

    [Test]
    public async Task SupersededExecutionAttempt_CannotReachRuntimePromotion()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new StubBackendProcessManager();
        var service = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
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
                    Arguments = [FixturePath(), "--delay", "1500"]
                }
            });
        var secondPackage = CreatePackage(appId, "second", hasRuntime: true);

        var firstTask = service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = firstPackage,
            Clean = true,
            ExecutionAttemptId = firstAttemptId
        });
        await Task.Delay(150);
        var secondTask = service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = secondPackage,
            Clean = true,
            ExecutionAttemptId = secondAttemptId
        });

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
        ApplicationBuildSpec? build = null)
    {
        var packagePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(root);
        var application = hasRuntime
            ? new ApplicationContract
            {
                Runtime = new ApplicationRuntimeSpec
                {
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
        return packagePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string FixturePath()
        => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "RunnerRuntimeFixture", "bin", "Debug", "net9.0", "RunnerRuntimeFixture.dll"));

    private sealed class StubBackendProcessManager : IAppRuntimeProcessManager
    {
        public List<Guid> StartedTargets { get; } = [];
        public List<Guid> StoppedTargets { get; } = [];
        public List<Guid> AppliedTargets { get; } = [];
        public Dictionary<Guid, ApplicationContract> StartedContracts { get; } = [];

        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null)
        {
            StartedTargets.Add(deploymentTargetId);
            AppliedTargets.Add(deploymentTargetId);
            StartedContracts[deploymentTargetId] = contract;
            if (contract.Runtime.Command is null)
                StoppedTargets.Add(deploymentTargetId);
            return Task.FromResult(new RuntimeApplyResult
            {
                PromotionIrreversible = contract.Runtime.Command is not null
            });
        }

        public Task StopAsync(Guid appId, Guid deploymentTargetId)
        {
            StoppedTargets.Add(deploymentTargetId);
            return Task.CompletedTask;
        }
    }
}
