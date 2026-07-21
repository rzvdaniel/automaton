using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class AppActionDeploymentServiceTests
{
    private string root = null!;
    private string? originalRoot;
    private RunnerWorkspaceService workspace = null!;
    private AppActionDeploymentService service = null!;

    [SetUp]
    public void SetUp()
    {
        originalRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        root = Path.Combine(Path.GetTempPath(), "automaton-action-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        workspace = new RunnerWorkspaceService();
        service = new AppActionDeploymentService(workspace);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalRoot);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task PrepareAll_SkipsUnchangedAndPreservesRestorableVersions()
    {
        var appId = Guid.NewGuid();
        var source = CreateBashAction(appId, "printf '{\"version\":1}'");

        var first = await service.PrepareAllAsync(appId);
        var unchanged = await service.PrepareAllAsync(appId);
        File.WriteAllText(Path.Combine(source, "main.sh"), "printf '{\"version\":2}'");
        var second = await service.PrepareAllAsync(appId);
        var versions = service.ListVersions(appId, "hello");

        Assert.That(first.Single().Status, Is.EqualTo("deployed"));
        Assert.That(unchanged.Single().Status, Is.EqualTo("unchanged"));
        Assert.That(second.Single().Status, Is.EqualTo("deployed"));
        Assert.That(versions.Versions, Has.Count.EqualTo(2));
        var oldVersion = versions.Versions.Single(version => !version.IsCurrent);

        var restored = service.RestoreVersion(appId, "hello", oldVersion.Version);

        Assert.That(restored.Success, Is.True);
        Assert.That(File.ReadAllText(Path.Combine(workspace.GetAppActionCurrentDirectory(appId, "hello"), "main.sh")), Does.Contain("version\":1"));
    }

    [Test]
    public async Task DeleteVersion_RejectsCurrentAndDeletesNamedOldVersion()
    {
        var appId = Guid.NewGuid();
        var source = CreateBashAction(appId, "printf '{}'");
        await service.PrepareAllAsync(appId);
        File.AppendAllText(Path.Combine(source, "main.sh"), "\nprintf ' '");
        await service.PrepareAllAsync(appId);
        var oldVersion = service.ListVersions(appId, "hello").Versions.Single(version => !version.IsCurrent);

        Assert.That(() => service.DeleteVersion(appId, "hello", "current"), Throws.TypeOf<InvalidOperationException>());
        Assert.That(service.DeleteVersion(appId, "hello", oldVersion.Version).Success, Is.True);
        Assert.That(service.ListVersions(appId, "hello").Versions, Has.Count.EqualTo(1));
    }

    private string CreateBashAction(Guid appId, string script)
    {
        var directory = Path.Combine(workspace.GetAppSourceRoot(appId), "actions", "hello");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "action.yaml"), """
            apiVersion: automaton.dev/v1
            kind: Action
            metadata:
              key: hello
            spec:
              runtime: bash
              entrypoint: main.sh
              timeoutSeconds: 30
            """);
        File.WriteAllText(Path.Combine(directory, "main.sh"), script);
        return directory;
    }
}
