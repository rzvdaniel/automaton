using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerFailedDeploymentScenario
{
    public static async Task RunAsync(string workspaceRoot)
    {
        var context = CreateContext(workspaceRoot);
        try
        {
            await AssertOriginalDeploymentAsync(context);
            var original = await CaptureCurrentArtifactAsync(context);
            var failedResult = await DeployPackageAsync(context, context.FailedPackage);
            await AssertFailedDeploymentPreservedOriginalAsync(context, original, failedResult);
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
            new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var originalPackage = CreateRuntimePackage(
            workspaceRoot,
            appId,
            ["RunnerRuntimeFixture.dll", "--tcp", "original"],
            "current");
        var failedPackage = CreateRuntimePackage(workspaceRoot, appId, ["missing-runtime-fixture.dll"], "candidate");
        return new ScenarioContext(workspace, registry, manager, deployment, appId, targetId, originalPackage, failedPackage);
    }

    private static async Task AssertOriginalDeploymentAsync(ScenarioContext context)
    {
        var result = await DeployPackageAsync(context, context.OriginalPackage);
        Assert.That(result.Success, Is.True, result.Message);
    }

    private static async Task<OriginalArtifact> CaptureCurrentArtifactAsync(ScenarioContext context)
    {
        var metadataPath = Path.Combine(
            context.Workspace.GetAppAutomationRoot(context.AppId, context.TargetId),
            "deployment.json");
        var metadata = ReadMetadata(metadataPath);
        var markerPath = Path.Combine(metadata.ArtifactRoot!, "marker.txt");
        var markerContent = await File.ReadAllTextAsync(markerPath);
        Assert.That(markerContent, Is.EqualTo("current"));
        Assert.That(context.Registry.TryGet(context.AppId, context.TargetId, out var route), Is.True);
        return new OriginalArtifact(metadataPath, metadata, markerPath, markerContent, route.Port);
    }

    private static Task<DeployAppPackageResult> DeployPackageAsync(ScenarioContext context, string packagePath)
        => context.Deployment.DeployPackage(new DeployAppPackageRequest
        {
            AppId = context.AppId,
            DeploymentTargetId = context.TargetId,
            PackagePath = packagePath,
            Clean = true
        });

    private static async Task AssertFailedDeploymentPreservedOriginalAsync(
        ScenarioContext context,
        OriginalArtifact original,
        DeployAppPackageResult failedResult)
    {
        var restored = ReadMetadata(original.MetadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(failedResult.Success, Is.False);
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

    private sealed record OriginalArtifact(
        string MetadataPath,
        RunnerRuntimeDeploymentMetadata Metadata,
        string MarkerPath,
        string MarkerContent,
        int Port);
}
