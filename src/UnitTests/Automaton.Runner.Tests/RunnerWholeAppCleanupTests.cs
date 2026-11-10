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

    [TestCase(true)]
    [TestCase(false)]
    public async Task WholeAppCleanup_WhenTheRunnerCannotDelete_EmptiesTheAppDirectoriesWithTheHelperAndRetries(bool helperWorks)
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") Assert.Ignore("Needs a non-root Unix user.");
        var workspace = new RunnerWorkspaceService();
        var service = new RunnerDeploymentService(workspace, new CleanupProcessManager(), new AppActionDeploymentService(workspace));
        var appId = Guid.NewGuid();
        var data = workspace.GetAppDataDirectory(appId);
        Directory.CreateDirectory(data);
        var parent = Path.GetDirectoryName(data)!;
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute); // the directory cannot be removed
        var purged = new List<string>();
        var cleanup = new MachineApplicationStorageCleanup((args, _) =>
        {
            if (args[1] != "run") return Task.FromResult(new InfrastructureCommandResponse { Success = true });
            purged.Add(args.First(a => a.StartsWith("type=bind,source=")));
            if (helperWorks) File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return Task.FromResult(new InfrastructureCommandResponse { Success = helperWorks, Error = "no such image" });
        });
        try
        {
            if (helperWorks)
            {
                await service.CleanupApplicationAsync(appId, Guid.NewGuid(), cleanup);
                Assert.That(Directory.Exists(data), Is.False);
            }
            else
            {
                var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupApplicationAsync(appId, Guid.NewGuid(), cleanup));
                Assert.That(error!.Message, Does.Contain("preserved for retry").And.Contain("no such image").And.Contain("First error"));
                Assert.That(error.InnerException, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                Assert.That(Directory.Exists(data), Is.True);
            }
            Assert.That(purged.Any(p => p.Contains(appId.ToString())), Is.True);
        }
        finally { File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    [Test]
    public async Task EmptyDirectory_RunsAnIsolatedHelperAndReportsFailure()
    {
        IReadOnlyList<string>? seen = null;
        var cleanup = new MachineApplicationStorageCleanup((args, _) =>
        {
            seen = args;
            return Task.FromResult(new InfrastructureCommandResponse { Success = args.Contains("find") });
        }, "helper:image");
        await cleanup.EmptyDirectoryAsync(Path.Combine(root, "data"));
        Assert.That(seen, Does.Contain("none").And.Contain("--cap-drop").And.Contain("helper:image"));
        Assert.That(seen!.Contains("/purge"), Is.True);
        Assert.That(string.Join(' ', seen!), Does.Contain("--user 0").And.Contain("find /purge -mindepth 1 -delete"));
        var failing = new MachineApplicationStorageCleanup((_, _) => Task.FromResult(new InfrastructureCommandResponse { Success = false }));
        Assert.ThrowsAsync<InvalidOperationException>(() => failing.EmptyDirectoryAsync(root));
    }

    [Test]
    public async Task EmptyDirectory_OnAContainerizedRunnerMountsItsOwnWorkspaceVolume_AndOnlyEmptiesThePathUnderIt()
    {
        var calls = new List<IReadOnlyList<string>>();
        var cleanup = new MachineApplicationStorageCleanup((args, _) =>
        {
            calls.Add(args);
            return Task.FromResult(new InfrastructureCommandResponse
            {
                Success = true,
                Output = args[1] == "inspect"
                    ? """[{"Type":"bind","Name":"","Destination":"/var/run/docker.sock"},{"Type":"volume","Name":"ws-volume","Destination":"/automaton/storage"}]"""
                    : ""
            });
        }, "helper:image", useWorkspaceVolume: true, ownContainer: "runner-container");

        await cleanup.EmptyDirectoryAsync("/automaton/storage/workspace/data/app-1");

        Assert.That(calls[0], Is.EqualTo(new[] { "docker", "inspect", "--format", "{{json .Mounts}}", "runner-container" }));
        var run = string.Join(' ', calls[1]);
        Assert.That(run, Does.Contain("type=volume,source=ws-volume,target=/automaton/storage").And.Contain("--user 0")
            .And.Contain("find /automaton/storage/workspace/data/app-1 -mindepth 1 -delete").And.Contain("--network none"));
        Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.EmptyDirectoryAsync("/etc"));
        Assert.That(calls.Count(call => call.Contains("find")), Is.EqualTo(1));
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
