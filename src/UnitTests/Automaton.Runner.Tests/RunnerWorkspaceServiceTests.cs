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
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

        Assert.That(service.WorkspaceRoot, Is.EqualTo(fullWorkspaceRoot));
        Assert.That(service.GetAppBackendDirectory(appId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "backend")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "data", appId.ToString())));
        Assert.That(service.GetAppVenvDirectory(appId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "venv")));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps")));
    }

    [Test]
    public void UsesRunnerScriptsDirAsFastApiCompatibilityBridge_WhenProvided()
    {
        var scriptsRoot = Path.Combine(Path.GetTempPath(), "runner-apps", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", scriptsRoot);
        var appId = Guid.NewGuid();

        Assert.That(service.UsesLegacyScriptsLayout, Is.True);
        Assert.That(service.AppsRoot, Is.EqualTo(Path.GetFullPath(scriptsRoot)));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.GetFullPath(scriptsRoot)));
        Assert.That(service.GetAppBackendDirectory(appId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "backend")));
        Assert.That(service.GetAppVenvDirectory(appId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "venv")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.Combine(Path.GetFullPath(scriptsRoot), appId.ToString(), "data")));
    }

    [Test]
    public void ResolvesRunnerDataFilesUnderWorkspaceDataRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();

        var resolved = service.ResolveRunnerFilePath(appId, "nested/file.txt", AppRunnerFileAreas.Data);

        Assert.That(resolved, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), "data", appId.ToString(), "nested", "file.txt")));
    }

    [Test]
    public void RejectsTraversalOutsideWorkspaceRoots()
    {
        var appId = Guid.NewGuid();

        Assert.That(() => service.ResolveBackendUploadPath(appId, "../escape.py"), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveRunnerFilePath(appId, "..\\escape.txt", AppRunnerFileAreas.Data), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveAppMediaFilePath(appId, "/etc/passwd"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void UsesLocalWorkspaceFallback_WhenWorkspaceRootIsNotConfigured()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "workspace"));

        Assert.That(service.WorkspaceRoot, Is.EqualTo(expected));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(expected, "apps")));
    }
}
