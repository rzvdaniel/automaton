using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class HostContainerRuntimeVolumePathTests
{
    [Test]
    public void VolumeSubpathsMatchExistingWorkspaceAndStorageModel()
    {
        using var testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
        var workspace = new RunnerWorkspaceService();
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var candidate = Guid.NewGuid();
        var artifactPath = workspace.GetAppRuntimeCandidateApplicationDirectory(appId, targetId, candidate);
        var relativeArtifactPath = Path.GetRelativePath(workspace.WorkspaceRoot, artifactPath).Replace('\\', '/');
        var dataPath = workspace.ResolveApplicationDataPath(appId, "database");
        var relativeDataPath = Path.GetRelativePath(workspace.WorkspaceRoot, dataPath).Replace('\\', '/');

        Assert.That(
            RunnerWorkspaceService.GetRuntimeCandidateVolumeSubpath(appId, targetId, candidate),
            Is.EqualTo("workspace/" + relativeArtifactPath));
        Assert.That(
            RunnerWorkspaceService.GetApplicationDataVolumeSubpath(appId, "database"),
            Is.EqualTo("workspace/" + relativeDataPath));
    }
}
