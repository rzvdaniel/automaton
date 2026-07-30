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

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(
            Path.GetTempPath(),
            "automaton-target-deployment-tests",
            Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
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
            new RunnerBackendEnvironmentService(workspace),
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
            workspace.GetAppRuntimeFrontendDirectory(appId, firstTargetId),
            "index.html"));
        var secondContent = await File.ReadAllTextAsync(Path.Combine(
            workspace.GetAppRuntimeFrontendDirectory(appId, secondTargetId),
            "index.html"));
        var firstMetadata = await File.ReadAllTextAsync(Path.Combine(
            workspace.GetDeploymentTargetRoot(appId, firstTargetId),
            "deployment.json"));
        var secondMetadata = await File.ReadAllTextAsync(Path.Combine(
            workspace.GetDeploymentTargetRoot(appId, secondTargetId),
            "deployment.json"));

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.True);
            Assert.That(first.DeploymentTargetId, Is.EqualTo(firstTargetId));
            Assert.That(second.DeploymentTargetId, Is.EqualTo(secondTargetId));
            Assert.That(firstContent, Is.EqualTo("first target"));
            Assert.That(secondContent, Is.EqualTo("second target"));
            Assert.That(firstMetadata, Does.Contain(firstTargetId.ToString()));
            Assert.That(secondMetadata, Does.Contain(secondTargetId.ToString()));
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
            new RunnerBackendEnvironmentService(workspace),
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

    private string CreatePackage(Guid appId, string frontendContent)
    {
        var packagePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(root);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "manifest.json",
            JsonSerializer.Serialize(new AppDeploymentPackageManifest
            {
                AppId = appId.ToString(),
                HasBackend = false
            }));
        WriteEntry(archive, "runtime/frontend/index.html", frontendContent);
        return packagePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class StubBackendProcessManager : IAppBackendProcessManager
    {
        public List<Guid> StoppedTargets { get; } = [];

        public Task EnsureRunningAsync(
            Guid appId,
            Guid deploymentTargetId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(Guid appId, Guid deploymentTargetId)
        {
            StoppedTargets.Add(deploymentTargetId);
            return Task.CompletedTask;
        }
    }
}
