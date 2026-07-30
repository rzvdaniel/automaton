using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerWorkspaceServiceTests
{
    private RunnerWorkspaceService service = null!;
    private string? originalWorkspaceRoot;
    private string? originalScriptsDir;

    [SetUp]
    public void SetUp()
    {
        originalWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsDir = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", null);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
        service = new RunnerWorkspaceService();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsDir);
    }

    [Test]
    public void UsesWorkspaceRootLayout_WhenWorkspaceRootIsConfigured()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);

        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

        Assert.That(service.WorkspaceRoot, Is.EqualTo(fullWorkspaceRoot));
        Assert.That(service.GetAppBackendDirectory(appId, targetId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "targets", targetId.ToString(), "runtime", "backend")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "data", appId.ToString())));
        Assert.That(service.GetAppVenvDirectory(appId, targetId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "targets", targetId.ToString(), "venv")));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps")));
    }

    [Test]
    public void UsesRunnerScriptsDirAsFastApiCompatibilityBridge_WhenProvided()
    {
        var scriptsRoot = Path.Combine(Path.GetTempPath(), "runner-apps", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", scriptsRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        Assert.That(service.UsesLegacyScriptsLayout, Is.True);
        Assert.That(service.AppsRoot, Is.EqualTo(Path.GetFullPath(scriptsRoot)));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.GetFullPath(scriptsRoot)));
        Assert.That(service.GetAppBackendDirectory(appId, targetId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "targets", targetId.ToString(), "runtime", "backend")));
        Assert.That(service.GetAppVenvDirectory(appId, targetId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "targets", targetId.ToString(), "venv")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "data")));
    }

    [Test]
    public void ResolvesRunnerDataFilesUnderWorkspaceDataRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var resolved = service.ResolveRunnerFilePath(appId, targetId, "nested/file.txt", AppRunnerFileAreas.Data);

        Assert.That(resolved, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), "data", appId.ToString(), "nested", "file.txt")));
    }

    [Test]
    public void ResolvesActionRuntimePathsUnderAppRuntimeRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var current = service.GetAppActionCurrentDirectory(appId, targetId, "send-email");

        Assert.That(current, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), "apps", appId.ToString(), "targets", targetId.ToString(), "runtime", "actions", "send-email", "current")));
        Assert.That(() => service.GetAppActionCurrentDirectory(appId, targetId, "../escape"), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RejectsTraversalOutsideWorkspaceRoots()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        Assert.That(() => service.ResolveBackendUploadPath(appId, targetId, "../escape.py"), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveRunnerFilePath(appId, targetId, "..\\escape.txt", AppRunnerFileAreas.Data), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveAppMediaFilePath(appId, "/etc/passwd"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void UsesLocalWorkspaceFallback_WhenWorkspaceRootIsNotConfigured()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "workspace"));

        Assert.That(service.WorkspaceRoot, Is.EqualTo(expected));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(expected, "apps")));
    }

    [Test]
    public void RuntimePaths_AreIsolatedByDeploymentTarget()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "runner-workspace",
            Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();

        var firstBackend = service.GetAppBackendDirectory(appId, firstTargetId);
        var secondBackend = service.GetAppBackendDirectory(appId, secondTargetId);
        var firstLog = Path.Combine(
            service.GetDeploymentTargetRoot(appId, firstTargetId),
            "logs",
            "backend.log");
        var secondLog = Path.Combine(
            service.GetDeploymentTargetRoot(appId, secondTargetId),
            "logs",
            "backend.log");

        Assert.Multiple(() =>
        {
            Assert.That(firstBackend, Is.Not.EqualTo(secondBackend));
            Assert.That(firstLog, Is.Not.EqualTo(secondLog));
            Assert.That(firstBackend, Does.Contain(
                Path.Combine("targets", firstTargetId.ToString())));
            Assert.That(secondBackend, Does.Contain(
                Path.Combine("targets", secondTargetId.ToString())));
        });
    }

    [Test]
    public void BackendRegistry_AllowsTwoTargetsOfOneApp()
    {
        var registry = new RunnerBackendRegistry();
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();

        registry.Register(appId, firstTargetId, 18001);
        registry.Register(appId, secondTargetId, 18002);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, firstTargetId, out var first), Is.True);
            Assert.That(first.Port, Is.EqualTo(18001));
            Assert.That(registry.TryGet(appId, secondTargetId, out var second), Is.True);
            Assert.That(second.Port, Is.EqualTo(18002));
        });

        registry.Remove(appId, firstTargetId);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, firstTargetId, out _), Is.False);
            Assert.That(registry.TryGet(appId, secondTargetId, out _), Is.True);
        });
    }

    [Test]
    public void RuntimePaths_RejectMissingTargetIdentity()
    {
        Assert.That(
            () => service.GetAppBackendDirectory(Guid.NewGuid(), Guid.Empty),
            Throws.TypeOf<ArgumentException>());
    }
}
