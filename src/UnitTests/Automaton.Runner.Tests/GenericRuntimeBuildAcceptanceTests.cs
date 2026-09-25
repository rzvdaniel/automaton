using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class GenericRuntimeBuildAcceptanceTests
{
    [Test]
    public async Task NodeBuildOutput_IsPassedToTheGenericRuntimeArtifactRoot()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new GenericRuntimeAcceptanceSupport.RecordingRuntimeProcessManager();
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace),
            new LocalApplicationBuildExecutor());
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = GenericRuntimeAcceptanceSupport.CreatePackage(
            GenericRuntimeAcceptanceSupport.AcceptanceFixture.Node,
            appId,
            "artifact",
            0);

        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Logs ?? []));
                Assert.That(processManager.ArtifactRoot, Is.Not.Null);
                Assert.That(File.Exists(Path.Combine(processManager.ArtifactRoot!, "out", "server.mjs")), Is.True);
                Assert.That(File.Exists(Path.Combine(processManager.ArtifactRoot!, "out", "build-marker.txt")), Is.True);
            });
        }
        finally
        {
            if (File.Exists(packagePath))
                File.Delete(packagePath);
        }
    }
}
