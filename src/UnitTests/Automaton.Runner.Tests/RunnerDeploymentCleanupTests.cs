using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerDeploymentCleanupTests
{
    private string workspaceRoot = null!;
    private string? previousWorkspaceRoot;

    [SetUp]
    public void SetUp()
    {
        previousWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-deployment-cleanup", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousWorkspaceRoot);
        if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, recursive: true);
    }

    [Test]
    public async Task FailedBuildRemovesOnlyItsAttemptWorkspace()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var package = CreateBuildPackage(appId);
        var workspace = new RunnerWorkspaceService();
        var service = new RunnerDeploymentService(
            workspace,
            new NoOpRuntimeProcessManager(),
            new AppActionDeploymentService(workspace),
            new FailingBuildExecutor());

        var result = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            ExecutionAttemptId = attemptId,
            PackagePath = package,
            Clean = true
        });

        Assert.That(result.Success, Is.False);
        Assert.That(Directory.Exists(workspace.GetDeploymentStagingDirectory(appId, targetId, attemptId)), Is.False);
        Assert.That(Directory.Exists(workspace.WorkspaceRoot), Is.True);
    }

    [Test]
    public async Task ResultJournalFailureDoesNotReportPromotedRuntimeAsFailed()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var package = CreateRuntimePackage(appId);
        var workspace = new RunnerWorkspaceService();
        Directory.CreateDirectory(workspace.RunnerRoot);
        File.WriteAllText(Path.Combine(workspace.RunnerRoot, "command-results"), "blocks journal directory");
        var runtimeManager = new PromotingRuntimeProcessManager();
        var service = new RunnerDeploymentService(
            workspace, runtimeManager, new AppActionDeploymentService(workspace));

        var result = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = package,
            DeploymentOperationId = operationId,
            ExecutionAttemptId = attemptId
        });

        Assert.That(result.Success, Is.True);
        Assert.That(runtimeManager.ApplyCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeploymentRetryAfterRunnerInterruptionReturnsSavedResultWithoutApplyingRuntimeAgain()
    {
        var workspace = new RunnerWorkspaceService();
        var operationId = Guid.NewGuid();
        var firstAttemptId = Guid.NewGuid();
        var savedResult = new DeployAppPackageResult
        {
            DeploymentTargetId = Guid.NewGuid(),
            Success = true,
            Message = "promoted before Runner interruption"
        };
        var journal = new HostedRunnerDeploymentResultJournal(workspace);
        await journal.SaveAsync(new DeployAppPackageRequest
        {
            DeploymentOperationId = operationId,
            ExecutionAttemptId = firstAttemptId
        }, savedResult, CancellationToken.None);

        var runtimeManager = new PromotingRuntimeProcessManager();
        var service = new RunnerDeploymentService(
            workspace,
            runtimeManager,
            new AppActionDeploymentService(workspace));
        var retryResult = await service.DeployPackage(new DeployAppPackageRequest
        {
            DeploymentOperationId = operationId,
            ExecutionAttemptId = Guid.NewGuid(),
            PackagePath = Path.Combine(workspaceRoot, "package-must-not-be-read.zip")
        });

        Assert.That(retryResult.Success, Is.True);
        Assert.That(retryResult.Message, Is.EqualTo(savedResult.Message));
        Assert.That(runtimeManager.ApplyCount, Is.Zero);
    }

    private string CreateBuildPackage(Guid appId)
    {
        var path = Path.Combine(workspaceRoot, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(workspaceRoot);
        var application = ApplicationContractDefaults.Create();
        application.Runtime.Image = "test/runtime:1";
        application.Runtime.Command = new ApplicationCommandSpec { Executable = "serve" };
        application.Build = new ApplicationBuildSpec
        {
            Image = "test/build:1",
            Command = new ApplicationCommandSpec { Executable = "build" }
        };

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = application
        }));
        WriteEntry(archive, "source/placeholder.txt", "source");
        WriteEntry(archive, "runtime/application/index.html", "runtime");
        return path;
    }

    private string CreateRuntimePackage(Guid appId)
    {
        var path = Path.Combine(workspaceRoot, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(workspaceRoot);
        var application = ApplicationContractDefaults.Create();
        application.Runtime.Image = "test/runtime:1";
        application.Runtime.Command = new ApplicationCommandSpec { Executable = "serve" };
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = application
        }));
        WriteEntry(archive, "source/placeholder.txt", "source");
        WriteEntry(archive, "runtime/application/index.html", "runtime");
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private sealed class FailingBuildExecutor : IRunnerApplicationBuildExecutor
    {
        public Task<ApplicationBuildExecutionResult> ExecuteAsync(
            ApplicationBuildSpec build,
            ApplicationRuntimeSpec runtime,
            string workspaceRoot,
            RunnerApplicationBuildContext? context = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("intentional build failure");
    }

    private sealed class NoOpRuntimeProcessManager : IAppRuntimeProcessManager
    {
        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null,
            Guid? candidateId = null)
            => Task.FromResult(new RuntimeApplyResult());

        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }

    private sealed class PromotingRuntimeProcessManager : IAppRuntimeProcessManager
    {
        public int ApplyCount { get; private set; }

        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null,
            Guid? candidateId = null)
        {
            ApplyCount++;
            return Task.FromResult(new RuntimeApplyResult { PromotionIrreversible = true });
        }

        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }
}
