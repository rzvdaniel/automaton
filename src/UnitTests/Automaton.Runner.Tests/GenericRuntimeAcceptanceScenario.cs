using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal static class GenericRuntimeAcceptanceScenario
{
    public static async Task RunAsync(string workspaceRoot)
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = GenericRuntimeAcceptanceSupport.CreateProcessManager(workspace, registry);
        var deployment = GenericRuntimeAcceptanceSupport.CreateDeployment(workspace, processManager);
        await using var gateway = await GenericRuntimeAcceptanceSupport.StartGatewayAsync(registry);

        foreach (var fixture in new[]
        {
            GenericRuntimeAcceptanceSupport.AcceptanceFixture.AspNetCore,
            GenericRuntimeAcceptanceSupport.AcceptanceFixture.Node,
            GenericRuntimeAcceptanceSupport.AcceptanceFixture.Python
        })
        {
            await VerifyFixtureAsync(fixture, workspaceRoot, workspace, processManager, deployment, gateway);
        }
    }

    private static async Task VerifyFixtureAsync(
        GenericRuntimeAcceptanceSupport.AcceptanceFixture fixture,
        string workspaceRoot,
        RunnerWorkspaceService workspace,
        RunnerRuntimeProcessManager processManager,
        RunnerDeploymentService deployment,
        GenericRuntimeAcceptanceSupport.GatewayHarness gateway)
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = GenericRuntimeAcceptanceSupport.CreatePackage(fixture, appId, $"{fixture.Name}-v1", 0);

        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });

            Assert.That(
                result.Success,
                Is.True,
                result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Logs ?? []));
            var metadata = await GenericRuntimeAcceptanceSupport.ReadMetadataAsync(workspace, appId, targetId);
            var body = await gateway.GetTextAsync(appId, targetId, "/hello?from=stage7");
            var artifactRoot = metadata.ArtifactRoot!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(result.SourceFilesDeployed, Is.GreaterThan(0));
                Assert.That(result.RuntimeFilesDeployed, Is.GreaterThan(0));
                Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(metadata.ProcessId, Is.Not.Null);
                Assert.That(body, Does.Contain($"{fixture.Name}:{fixture.Name}-v1:/hello?from=stage7"));
                Assert.That(body, Does.Contain(workspace.GetAppDataDirectory(appId)));
                Assert.That(File.Exists(Path.Combine(artifactRoot, fixture.BuiltArtifact)), Is.True);
            });
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            GenericRuntimeAcceptanceSupport.DeleteFileIfExists(packagePath);
        }
    }
}
