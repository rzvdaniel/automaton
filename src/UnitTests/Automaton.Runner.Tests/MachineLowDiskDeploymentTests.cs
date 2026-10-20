using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class MachineLowDiskDeploymentTests
{
    [Test]
    public async Task RefusalCleansAttemptStagingAndPreservesCurrentRuntime()
    {
        var outcome = await MachineLowDiskDeploymentScenario.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(outcome.InitialDeploySucceeded, Is.True);
            Assert.That(outcome.RefusalMessage, Does.Contain("configured minimum is 1024 MB"));
            Assert.That(outcome.CurrentRuntimeContent, Is.EqualTo("currently healthy runtime"));
            Assert.That(outcome.ApplyCountAfterRefusal, Is.EqualTo(outcome.ApplyCountBeforeRefusal));
            Assert.That(outcome.StagingExistsAfterRefusal, Is.False);
        });
    }
}

internal sealed record MachineLowDiskDeploymentOutcome(
    bool InitialDeploySucceeded,
    string? RefusalMessage,
    string CurrentRuntimeContent,
    int ApplyCountBeforeRefusal,
    int ApplyCountAfterRefusal,
    bool StagingExistsAfterRefusal);

internal static class MachineLowDiskDeploymentScenario
{
    public static async Task<MachineLowDiskDeploymentOutcome> RunAsync()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-low-disk-deploy", Guid.NewGuid().ToString("N"));
        var previousRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var previousProfile = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE");
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", "MachineManagement");
        try
        {
            using var fixture = new MachineLowDiskDeploymentFixture(workspaceRoot);
            return await fixture.RunAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", previousProfile);
            if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, recursive: true);
        }
    }
}

internal sealed class MachineLowDiskDeploymentFixture : IDisposable
{
    private const string CurrentRuntimeText = "currently healthy runtime";
    private readonly string root;
    private readonly RunnerWorkspaceService workspace;
    private readonly RecordingRuntimeManager runtimeManager;
    private readonly RunnerDeploymentService deployments;
    private long availableBytes = 2L * 1024 * 1024 * 1024;
    private readonly Guid appId = Guid.NewGuid();
    private readonly Guid targetId = Guid.NewGuid();

    public MachineLowDiskDeploymentFixture(string root)
    {
        this.root = root;
        workspace = new RunnerWorkspaceService();
        runtimeManager = new RecordingRuntimeManager();
        deployments = new RunnerDeploymentService(workspace, runtimeManager,
            new AppActionDeploymentService(workspace),
            diskAdmission: new MachineWorkloadDiskAdmission(_ => availableBytes));
    }

    public async Task<MachineLowDiskDeploymentOutcome> RunAsync()
    {
        var policy = new ActionContainerPolicy(new(1m, 1024, 256), 64, 20, 5, "python:3.12-slim");
        var initial = await DeployAsync(CreatePackage("healthy"), policy, null);
        var currentFile = Path.Combine(workspace.GetAppRuntimeApplicationDirectory(appId, targetId), "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(currentFile)!);
        await File.WriteAllTextAsync(currentFile, CurrentRuntimeText);
        var applyCountBeforeRefusal = runtimeManager.ApplyCount;
        var attemptId = Guid.NewGuid();
        availableBytes = 0;

        var refusal = await DeployAsync(CreatePackage("replacement"), policy, attemptId);
        return new MachineLowDiskDeploymentOutcome(initial.Success, refusal.Message,
            await File.ReadAllTextAsync(currentFile), applyCountBeforeRefusal, runtimeManager.ApplyCount,
            Directory.Exists(workspace.GetDeploymentStagingDirectory(appId, targetId, attemptId)));
    }

    private Task<DeployAppPackageResult> DeployAsync(string package, ActionContainerPolicy policy, Guid? attemptId)
        => deployments.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = package,
            Clean = attemptId.HasValue,
            ExecutionAttemptId = attemptId,
            ActionContainerPolicy = policy
        });

    private string CreatePackage(string content)
    {
        var path = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        var application = ApplicationContractDefaults.Create();
        application.Runtime.Image = "test/runtime:1";
        application.Runtime.Command = new ApplicationCommandSpec { Executable = "serve" };
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = application
        }));
        WriteEntry(archive, "runtime/application/index.html", content);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.zip")) File.Delete(file);
    }

    private sealed class RecordingRuntimeManager : IAppRuntimeProcessManager
    {
        public int ApplyCount { get; private set; }
        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId,
            ApplicationContract contract, CancellationToken cancellationToken = default,
            string? artifactRoot = null, Guid? candidateId = null, Guid? deploymentAttemptId = null)
        {
            ApplyCount++;
            return Task.FromResult(new RuntimeApplyResult());
        }
        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }
}
