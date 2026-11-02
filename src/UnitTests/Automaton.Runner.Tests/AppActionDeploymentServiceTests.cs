using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
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
    public void MachineManagementRejectsPythonDependencyPreparationBeforeCreatingEnvironment()
    {
        var previousProfile = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE");
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var directory = Path.Combine(workspace.GetAppSourceRoot(appId, targetId), "actions", "dependency-hook");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "action.yaml"), """
            apiVersion: automaton.dev/v1
            kind: Action
            metadata:
              key: dependency-hook
            spec:
              runtime: python
              entrypoint: main.py
            """);
        File.WriteAllText(Path.Combine(directory, "main.py"), "print('should not run')");
        File.WriteAllText(Path.Combine(directory, "requirements.txt"), "hook-package==1.0");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", "MachineManagement");
        try
        {
            Assert.That(async () => await service.PrepareAllAsync(appId, targetId),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(Directory.Exists(Path.Combine(
                workspace.GetAppActionCurrentDirectory(appId, targetId, "dependency-hook"), ".venv")), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", previousProfile);
        }
    }

    private string CreateBashAction(Guid appId, Guid targetId, string script)
    {
        var directory = Path.Combine(workspace.GetAppSourceRoot(appId, targetId), "actions", "hello");
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
