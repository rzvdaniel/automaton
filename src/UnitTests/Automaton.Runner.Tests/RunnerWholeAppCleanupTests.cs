using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
namespace Automaton.Runner.Console.Tests;
[TestFixture, NonParallelizable]
public sealed class RunnerWholeAppCleanupTests
{
    private string root = null!;
    private string? previous;
    [SetUp] public void SetUp()
    {
        previous = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(Path.GetTempPath(), "app-cleanup-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
    }
    [TearDown] public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previous);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task WholeAppCleanup_RemovesOnlyOwnedStorageAndPreservesItOnCleanupFailure(bool failRuntime, bool failVolume)
    {
        var fail = failRuntime || failVolume;
        var workspace = new RunnerWorkspaceService();
        var process = new CleanupProcessManager
        { StopFailure = failRuntime ? new InvalidOperationException("cleanup failed") : null };
        var service = new RunnerDeploymentService(workspace, process, new AppActionDeploymentService(workspace));
        var storageCleanup = CreateStorageCleanup(failVolume);
        var appId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var owned = new[] { workspace.GetAppRoot(appId), workspace.GetAppDataDirectory(appId),
            Path.Combine(workspace.RunnerRoot, "deployments", appId.ToString()) };
        foreach (var path in owned) Directory.CreateDirectory(path);
        Directory.CreateDirectory(workspace.GetAppDataDirectory(siblingId));
        var marker = Path.Combine(workspace.GetAppDataDirectory(siblingId), "marker");
        await File.WriteAllTextAsync(marker, "preserve");
        if (fail)
            Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupApplicationAsync(appId, targetId, storageCleanup));
        else
        {
            await service.CleanupApplicationAsync(appId, targetId, storageCleanup);
            await service.CleanupApplicationAsync(appId, targetId, storageCleanup);
        }
        Assert.That(owned.All(path => Directory.Exists(path) == fail), Is.True);
        Assert.That(await File.ReadAllTextAsync(marker), Is.EqualTo("preserve"));
    }

    private static MachineApplicationStorageCleanup CreateStorageCleanup(bool fail)
        => new((_, _) => Task.FromResult(new InfrastructureCommandResponse { Success = !fail }));

    [Test]
    public void WholeAppStorageCleanup_RemovesLegacyDataAndRejectsEmptyIdentity()
    {
        var previousScripts = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", null);
            Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", Path.Combine(root, "legacy"));
            var workspace = new RunnerWorkspaceService();
            var app = Guid.NewGuid();
            var legacyData = workspace.GetLegacyControllerDataDirectory(app);
            Directory.CreateDirectory(legacyData);
            Directory.CreateDirectory(workspace.GetAppDataDirectory(app));
            workspace.DeleteApplicationStorage(app);
            Assert.That(Directory.Exists(legacyData), Is.False);
            Assert.That(Directory.Exists(workspace.GetAppDataDirectory(app)), Is.False);
            Assert.Throws<ArgumentException>(() => workspace.DeleteApplicationStorage(Guid.Empty));
        }
        finally { Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", previousScripts); }
    }

    private sealed class CleanupProcessManager : IAppRuntimeProcessManager
    {
        public Exception? StopFailure { get; init; }
        public Task StopAsync(Guid appId, Guid deploymentTargetId)
            => StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId, ApplicationContract contract,
            CancellationToken cancellationToken = default, string? artifactRoot = null, Guid? candidateId = null,
            Guid? deploymentAttemptId = null)
            => throw new NotSupportedException();
    }
}
