using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerInPlaceBuildScenario
{
    public static async Task RunAsync(string workspaceRoot)
    {
        var context = CreateContext(workspaceRoot);
        try
        {
            var result = await DeployAsync(context);
            Assert.That(result.Success, Is.True, result.Message);
            AssertBuiltArtifactIsRuntime(context);
        }
        finally
        {
            await context.Manager.StopAsync(context.AppId, context.TargetId);
            context.Manager.Dispose();
            DeletePackage(context.PackagePath);
        }
    }

    private static ScenarioContext CreateContext(string workspaceRoot)
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        var manager = CreateManager(workspace, registry);
        var deployment = new RunnerDeploymentService(
            workspace,
            manager,
            new AppActionDeploymentService(workspace),
            new LocalApplicationBuildExecutor());
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var package = CreateRuntimePackage(
            workspaceRoot,
            appId,
            ["RunnerRuntimeFixture.dll", "--require-file", "built.txt", "--tcp", "in-place"],
            "in-place",
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), "--write-file", "built.txt", "created-by-build"],
                    WorkingDirectory = "."
                }
            });
        return new ScenarioContext(workspace, manager, deployment, appId, targetId, package);
    }

    private static Task<DeployAppPackageResult> DeployAsync(ScenarioContext context)
        => context.Deployment.DeployPackage(new DeployAppPackageRequest
        {
            AppId = context.AppId,
            DeploymentTargetId = context.TargetId,
            PackagePath = context.PackagePath,
            Clean = true
        });

    private static void AssertBuiltArtifactIsRuntime(ScenarioContext context)
    {
        var metadataPath = Path.Combine(
            context.Workspace.GetAppAutomationRoot(context.AppId, context.TargetId),
            "deployment.json");
        var metadata = ReadMetadata(metadataPath);
        var builtFile = Path.Combine(metadata.ArtifactRoot!, "built.txt");
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(builtFile), Is.EqualTo("created-by-build"));
            Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(IsProcessAlive(metadata.ProcessId!.Value), Is.True);
        });
    }

    private static void DeletePackage(string packagePath)
    {
        if (File.Exists(packagePath))
            File.Delete(packagePath);
    }

    private sealed record ScenarioContext(
        RunnerWorkspaceService Workspace,
        RunnerRuntimeProcessManager Manager,
        RunnerDeploymentService Deployment,
        Guid AppId,
        Guid TargetId,
        string PackagePath);
}
