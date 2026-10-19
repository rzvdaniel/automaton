using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class RunnerApplicationBuildContextTests
{
    private string root = null!;
    private string? originalRoot;
    private string? originalScripts;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScripts = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        root = Path.Combine(Path.GetTempPath(), "automaton-build-context-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScripts);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task ContainerBuildReceivesServerIssuedExecutionContext()
    {
        var workspace = new RunnerWorkspaceService();
        var build = new RecordingBuildExecutor();
        var service = new RunnerDeploymentService(workspace, new NoopRuntimeManager(),
            new AppActionDeploymentService(workspace), build);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var packagePath = CreatePackage(appId);

        var result = await service.DeployPackage(new DeployAppPackageRequest
        {
            AppId = appId,
            DeploymentTargetId = targetId,
            ExecutionAttemptId = attemptId,
            PackagePath = packagePath,
            Clean = true
        });

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(build.Context, Is.EqualTo(new RunnerApplicationBuildContext(appId, targetId, attemptId)));
    }

    private string CreatePackage(Guid appId)
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
                Image = "build:fixture",
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
        WriteEntry(archive, "source/placeholder.txt", "source");
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private sealed class NoopRuntimeManager : IAppRuntimeProcessManager
    {
        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId, ApplicationContract contract,
            CancellationToken cancellationToken = default, string? artifactRoot = null, Guid? candidateId = null,
            Guid? deploymentAttemptId = null)
            => Task.FromResult(new RuntimeApplyResult());

        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
    }

    private sealed class RecordingBuildExecutor : IRunnerApplicationBuildExecutor
    {
        public RunnerApplicationBuildContext? Context { get; private set; }

        public Task<ApplicationBuildExecutionResult> ExecuteAsync(ApplicationBuildSpec build,
            ApplicationRuntimeSpec runtime, string workspaceRoot, RunnerApplicationBuildContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(new ApplicationBuildExecutionResult { Succeeded = true });
        }
    }
}
