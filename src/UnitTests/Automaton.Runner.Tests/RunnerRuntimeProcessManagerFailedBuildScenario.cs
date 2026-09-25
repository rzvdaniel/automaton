using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerFailedBuildScenario
{
    public static async Task RunAsync(string workspaceRoot)
    {
        var context = CreateContext(workspaceRoot);
        try
        {
            await DeployOriginalAsync(context);
            var original = await CaptureOriginalRuntimeAsync(context);
            var failedResult = await DeployCandidateAsync(context);
            await AssertCurrentRuntimeWasPreservedAsync(context, original, failedResult);
        }
        finally
        {
            await context.Manager.StopAsync(context.AppId, context.TargetId);
            context.Manager.Dispose();
            DeletePackage(context.OriginalPackage);
            DeletePackage(context.FailedPackage);
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
        var originalPackage = CreateRuntimePackage(
            workspaceRoot,
            appId,
            ["RunnerRuntimeFixture.dll", "--tcp", "original"],
            "current");
        var failedPackage = CreateRuntimePackage(
            workspaceRoot,
            appId,
            ["RunnerRuntimeFixture.dll", "--tcp", "candidate"],
            "candidate",
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), "--fail-with-output"],
                    WorkingDirectory = "."
                }
            });

        return new ScenarioContext(workspace, registry, manager, deployment, appId, targetId, originalPackage, failedPackage);
    }

    private static async Task DeployOriginalAsync(ScenarioContext context)
    {
        var result = await DeployAsync(context, context.OriginalPackage);
        Assert.That(result.Success, Is.True, result.Message);
    }

    private static async Task<OriginalRuntime> CaptureOriginalRuntimeAsync(ScenarioContext context)
    {
        var metadataPath = Path.Combine(
            context.Workspace.GetAppAutomationRoot(context.AppId, context.TargetId),
            "deployment.json");
        var metadata = ReadMetadata(metadataPath);
        var markerPath = Path.Combine(metadata.ArtifactRoot!, "marker.txt");
        var markerContent = await File.ReadAllTextAsync(markerPath);
        Assert.That(context.Registry.TryGet(context.AppId, context.TargetId, out var route), Is.True);
        return new OriginalRuntime(metadataPath, metadata, markerPath, markerContent, route.Port);
    }

    private static Task<DeployAppPackageResult> DeployCandidateAsync(ScenarioContext context)
        => DeployAsync(context, context.FailedPackage);

    private static Task<DeployAppPackageResult> DeployAsync(ScenarioContext context, string packagePath)
        => context.Deployment.DeployPackage(new DeployAppPackageRequest
        {
            AppId = context.AppId,
            DeploymentTargetId = context.TargetId,
            PackagePath = packagePath,
            Clean = true
        });

    private static async Task AssertCurrentRuntimeWasPreservedAsync(
        ScenarioContext context,
        OriginalRuntime original,
        DeployAppPackageResult failedResult)
    {
        var restored = ReadMetadata(original.MetadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(failedResult.Success, Is.False);
            Assert.That(failedResult.Message, Does.Contain("Application build failed"));
            Assert.That(failedResult.Logs, Has.Some.EqualTo("stdout: useful stdout diagnostic"));
            Assert.That(IsProcessAlive(original.Metadata.ProcessId!.Value), Is.True);
            Assert.That(context.Registry.TryGet(context.AppId, context.TargetId, out var route), Is.True);
            Assert.That(route.Port, Is.EqualTo(original.Port));
            Assert.That(restored.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(restored.ProcessId, Is.EqualTo(original.Metadata.ProcessId));
            Assert.That(restored.ArtifactRoot, Is.EqualTo(original.Metadata.ArtifactRoot));
        });
        Assert.That(await File.ReadAllTextAsync(original.MarkerPath), Is.EqualTo(original.MarkerContent));
    }

    private static void DeletePackage(string packagePath)
    {
        if (File.Exists(packagePath))
            File.Delete(packagePath);
    }

    private sealed record ScenarioContext(
        RunnerWorkspaceService Workspace,
        RunnerRuntimeRegistry Registry,
        RunnerRuntimeProcessManager Manager,
        RunnerDeploymentService Deployment,
        Guid AppId,
        Guid TargetId,
        string OriginalPackage,
        string FailedPackage);

    private sealed record OriginalRuntime(
        string MetadataPath,
        RunnerRuntimeDeploymentMetadata Metadata,
        string MarkerPath,
        string MarkerContent,
        int Port);
}
