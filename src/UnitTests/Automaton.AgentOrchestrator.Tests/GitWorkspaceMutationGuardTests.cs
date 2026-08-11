using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitWorkspaceMutationGuardTests
{
    [Test]
    public async Task FingerprintIncludesTrackedDiffAndUntrackedFileContent()
    {
        var root = Directory.CreateTempSubdirectory("automaton-role-guard-");
        try
        {
            var runner = new ProcessGitCommandRunner();
            await GitAsync(runner, root.FullName, "init");
            await GitAsync(runner, root.FullName, "config", "user.email", "guard@example.invalid");
            await GitAsync(runner, root.FullName, "config", "user.name", "Guard Test");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "tracked.txt"), "initial\n");
            await GitAsync(runner, root.FullName, "add", "tracked.txt");
            await GitAsync(runner, root.FullName, "commit", "-m", "initial");
            var workspace = new Workspace("key", root.FullName, "branch", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "repo");
            var guard = new GitWorkspaceMutationGuard(runner);

            var clean = await guard.CaptureAsync(workspace, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "tracked.txt"), "changed\n");
            var tracked = await guard.CaptureAsync(workspace, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "new.txt"), "one\n");
            var untrackedOne = await guard.CaptureAsync(workspace, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "new.txt"), "two\n");
            var untrackedTwo = await guard.CaptureAsync(workspace, CancellationToken.None);

            Assert.That(new[] { clean, tracked, untrackedOne, untrackedTwo }.Distinct().ToArray(), Has.Length.EqualTo(4));
        }
        finally
        {
            if (root.Exists)
            {
                foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
                root.Delete(true);
            }
        }
    }

    private static async Task GitAsync(IGitCommandRunner runner, string root, params string[] arguments)
    {
        var result = await runner.RunAsync(root, arguments, CancellationToken.None);
        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
    }
}
