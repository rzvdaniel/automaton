using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture, NonParallelizable]
public sealed class RunnerDeploymentActionRollbackTests
{
    [Test]
    public void RestoreActionRootsRecoversPublishedActionsFromThePreviousSnapshot()
    {
        var previousRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "automaton-action-rollback", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        try
        {
            var workspace = new RunnerWorkspaceService();
            var appId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var deployment = new RunnerDeploymentWorkspace(workspace);
            var oldSourceAction = Path.Combine(workspace.GetAppSourceActionsDirectory(appId, targetId), "first");
            var oldRuntimeAction = workspace.GetAppActionCurrentDirectory(appId, targetId, "first");
            Write(Path.Combine(oldSourceAction, "main.py"), "old source");
            Write(Path.Combine(oldRuntimeAction, "main.py"), "old runtime");
            Write(Path.Combine(workspace.GetAppActionRuntimeRoot(appId, targetId, "first"), "versions", "v1", "main.py"), "old version");

            var snapshot = deployment.CreateSnapshotIfNeeded(appId, targetId);
            Write(Path.Combine(oldSourceAction, "main.py"), "new source");
            Write(Path.Combine(oldRuntimeAction, "main.py"), "new runtime");
            Write(Path.Combine(workspace.GetAppActionCurrentDirectory(appId, targetId, "second"), "main.py"), "partial new action");

            deployment.RestoreActionRoots(appId, targetId, snapshot);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(oldSourceAction, "main.py")), Is.EqualTo("old source"));
                Assert.That(File.ReadAllText(Path.Combine(oldRuntimeAction, "main.py")), Is.EqualTo("old runtime"));
                Assert.That(File.ReadAllText(Path.Combine(workspace.GetAppActionRuntimeRoot(appId, targetId, "first"), "versions", "v1", "main.py")),
                    Is.EqualTo("old version"));
                Assert.That(Directory.Exists(workspace.GetAppActionRuntimeRoot(appId, targetId, "second")), Is.False);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
