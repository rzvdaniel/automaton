using System.Diagnostics;
using System.IO;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerRuntimeProcessManagerStorageAndReplacementTests : RunnerRuntimeProcessManagerTestBase
{    [Test]
    public async Task RuntimeReceivesExplicitPersistentDataAndStorageMountPaths()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var manager = CreateManager(workspace, registry);
        var contract = CreateContract(["--tcp", "storage"]);
        contract.Storage = new ApplicationStorageSpec
        {
            DataDirectory = "app-data",
            Mounts = [new ApplicationStorageMount { Name = "uploads", RelativePath = "media/uploads" }]
        };

        await manager.ApplyAsync(appId, targetId, contract);
        var metadata = ReadMetadata(Path.Combine(
            workspace.GetAppAutomationRoot(appId, targetId),
            "deployment.json"));
        var outputPath = Path.Combine(
            workspace.GetAppRuntimeLogsDirectory(appId, targetId, metadata.ArtifactId!),
            "runtime.stdout.log");
        var output = await WaitForLogLineAsync(outputPath, "storage");

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain(workspace.ResolveApplicationDataPath(appId, "app-data")));
            Assert.That(output, Does.Contain(workspace.ResolveApplicationDataPath(appId, "media/uploads")));
        });
    }

    [Test]
    public async Task CurrentRuntimeExitDuringReplacementReadiness_DoesNotDisposeTheCandidate()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var manager = CreateManager(workspace, registry);
        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "original"]));

        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var original = ReadMetadata(metadataPath);
        Assert.That(original.ProcessId, Is.Not.Null);
        var replacement = CreateContract(["--startup-delay", "3000", "--tcp", "replacement"]);
        var replacementTask = manager.ApplyAsync(appId, targetId, replacement);
        var pending = await WaitForCandidateAsync(metadataPath);

        Assert.That(pending.Candidate?.ProcessId, Is.Not.Null);
        using (var oldProcess = Process.GetProcessById(original.ProcessId!.Value))
        {
            oldProcess.Kill(entireProcessTree: true);
            await oldProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.That(
            await WaitForOldRuntimeExitCallbackAsync(registry, appId, targetId, metadataPath, replacementTask),
            Is.True,
            "The old runtime callback should remove its route while the replacement candidate is still pending.");

        await replacementTask.WaitAsync(TimeSpan.FromSeconds(10));
        var promoted = ReadMetadata(metadataPath);

        Assert.Multiple(() =>
        {
            Assert.That(promoted.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(promoted.Candidate, Is.Null);
            Assert.That(promoted.ProcessId, Is.Not.EqualTo(original.ProcessId));
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await manager.StopAsync(appId, targetId);
    }
    [Test]
    public async Task TcpReadiness_DoesNotRequireHealthPath()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp"]));

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task HttpReadiness_UsesConfiguredHealthPathAndPort()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--http"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 5 };

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task HttpReadiness_RetriesNonSuccessResponsesUntilSuccess()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--http-503-once"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 5 };

        await manager.ApplyAsync(appId, targetId, contract);

        Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public void ProcessExitDuringStartupReportsAReadinessFailure()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contract = CreateContract(["--delay", "200"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 5 };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.ApplyAsync(appId, targetId, contract));

        Assert.That(exception!.Message, Does.Contain("Runtime process exited before becoming ready"));
        Assert.That(registry.TryGet(appId, targetId, out _), Is.False);
    }

    [Test]
    public void HttpReadiness_TimesOutWhenResponsesNeverSucceed()
    {
        var registry = new RunnerRuntimeRegistry();
        using var manager = CreateManager(new RunnerWorkspaceService(), registry);
        var contract = CreateContract(["--http-503"]);
        contract.Health = new ApplicationHealthSpec { Path = "/ready", StartupTimeoutSeconds = 1 };

        Assert.That(
            async () => await manager.ApplyAsync(Guid.NewGuid(), Guid.NewGuid(), contract),
            Throws.TypeOf<TimeoutException>());
    }

    [Test]
    public async Task ApplyingUpdatedRuntime_ReplacesTheProcessAfterReadiness()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var manager = CreateManager(workspace, registry);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "old"]));
        var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var first = ReadMetadata(metadataPath);

        await manager.ApplyAsync(appId, targetId, CreateContract(["--tcp", "new"]));
        var second = ReadMetadata(metadataPath);

        Assert.Multiple(() =>
        {
            Assert.That(first.ProcessId, Is.Not.Null);
            Assert.That(second.ProcessId, Is.Not.EqualTo(first.ProcessId));
            Assert.That(second.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
        });

        await manager.StopAsync(appId, targetId);
    }

    [Test]
    public async Task DeployedRuntimeFixture_StartsFromTheGenericApplicationArtifactRoot()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var processManager = CreateManager(workspace, registry);
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = CreateRuntimePackage(root, appId);

        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });

            var metadataPath = Path.Combine(workspace.GetAppAutomationRoot(appId, targetId), "deployment.json");
            var deployedMetadata = ReadMetadata(metadataPath);
            var logPath = Path.Combine(
                workspace.GetAppRuntimeLogsDirectory(appId, targetId, deployedMetadata.ArtifactId!),
                "runtime.stdout.log");
            var output = await WaitForLogLineAsync(logPath, "deployed");
            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(output, Does.Contain("stdout:--tcp|deployed"));
                Assert.That(registry.TryGet(appId, targetId, out _), Is.True);
            });
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            processManager.Dispose();
            if (File.Exists(packagePath))
                File.Delete(packagePath);
        }
    }


}
