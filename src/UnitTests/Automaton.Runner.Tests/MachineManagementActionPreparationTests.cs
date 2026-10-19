using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class MachineManagementActionPreparationTests
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
        root = Path.Combine(Path.GetTempPath(), "automaton-machine-action-tests", Guid.NewGuid().ToString("N"));
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
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task ActionDependencyPreparationIsRejectedBeforeBuildOrWorkspaceMutation()
    {
        var workspace = new RunnerWorkspaceService();
        var runtime = new NoopRuntimeManager();
        var build = new RecordingBuildExecutor();
        var service = new RunnerDeploymentService(workspace, runtime, new AppActionDeploymentService(workspace), build);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = CreateActionPackage(appId);

        var result = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            PackagePath = packagePath,
            ExecutionAttemptId = Guid.NewGuid()
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Bounded Action dependency preparation is unavailable"));
            Assert.That(build.Context, Is.Null, "The build must not start before Action preparation is rejected.");
            Assert.That(runtime.ApplyCount, Is.Zero);
            Assert.That(Directory.Exists(workspace.GetAppSourceRoot(appId, targetId)), Is.False);
            Assert.That(Directory.Exists(workspace.GetAppActionCurrentDirectory(appId, targetId, "dependency-hook")), Is.False);
        });
    }

    private string CreateActionPackage(Guid appId)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        var contract = new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "runtime:fixture",
                Command = new ApplicationCommandSpec { Executable = "dotnet" }
            },
            Build = new ApplicationBuildSpec
            {
                Image = "build@sha256:fixture",
                Command = new ApplicationCommandSpec { Executable = "sh", Arguments = ["-c", "echo build"] }
            }
        };

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new AppDeploymentPackageManifest
        {
            AppId = appId.ToString(),
            Application = contract
        }));
        WriteEntry(archive, "runtime/application/index.html", "runtime");
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
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private sealed class NoopRuntimeManager : IAppRuntimeProcessManager
    {
        public int ApplyCount { get; private set; }

        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId, ApplicationContract contract,
            CancellationToken cancellationToken = default, string? artifactRoot = null, Guid? candidateId = null,
            Guid? deploymentAttemptId = null)
        {
            ApplyCount++;
            return Task.FromResult(new RuntimeApplyResult());
        }

        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }

    private sealed class RecordingBuildExecutor : IRunnerApplicationBuildExecutor
    {
        public RunnerApplicationBuildContext? Context { get; private set; }

        public Task<ApplicationBuildExecutionResult> ExecuteAsync(ApplicationBuildSpec build, ApplicationRuntimeSpec runtime,
            string workspaceRoot, RunnerApplicationBuildContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new ApplicationBuildExecutionResult { Succeeded = true });
        }
    }
}
