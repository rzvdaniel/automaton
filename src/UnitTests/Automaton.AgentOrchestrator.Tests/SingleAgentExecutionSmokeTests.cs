using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class SingleAgentExecutionSmokeTests
{
    [Test]
    [Category("Stage7RoleExecutionSmoke")]
    public async Task RealStage7RolesCreateAndApproveExpectedFileInDisposableWorktree()
    {
        var command = Environment.GetEnvironmentVariable("AUTOMATON_STAGE7_SMOKE_COMMAND");
        if (string.IsNullOrWhiteSpace(command)) Assert.Ignore("Set AUTOMATON_STAGE7_SMOKE_COMMAND for the controlled Stage 7 smoke test.");

        var root = Directory.CreateTempSubdirectory("automaton-stage7-smoke-");
        try
        {
            var canonical = Directory.CreateDirectory(Path.Combine(root.FullName, "canonical")).FullName;
            var workspaceRoot = Path.Combine(root.FullName, "workspaces");
            await GitAsync(canonical, "init");
            await GitAsync(canonical, "config", "user.email", "stage5-smoke@example.invalid");
            await GitAsync(canonical, "config", "user.name", "Stage 5 Smoke");
            await File.WriteAllTextAsync(Path.Combine(canonical, "README.md"), "Disposable Stage 5 smoke repository.\n");
            await GitAsync(canonical, "add", "README.md");
            await GitAsync(canonical, "commit", "-m", "Initial smoke fixture");

            var options = new OrchestratorOptions
            {
                Repository = new RepositoryOptions { Root = canonical, Identity = "smoke/disposable" },
                Workspace = new WorkspaceOptions { Root = workspaceRoot },
                Agent = new AgentExecutionOptions { MaxConcurrentAgents = 1, MaxTurns = 3, ManagerMaxTurns = 2, DeveloperMaxTurns = 3, ReviewerMaxTurns = 2 },
                Codex = new CodexOptions { Command = command, TurnTimeoutMilliseconds = 120_000 }
            };
            var workflow = new WorkflowDefinition(
                Path.Combine(canonical, "WORKFLOW.md"),
                new Dictionary<string, object?>(),
                "Keep this Stage 7 smoke task minimal. Manager and Reviewer remain read-only. Developer creates only the requested text file and verifies it.",
                options);
            var clock = new SystemClock();
            var workspaceManager = new GitWorktreeWorkspaceManager(workspaceRoot, new ProcessGitCommandRunner(), clock);
            var factory = new CodexAppServerClientFactory(workspaceManager, options, clock, new PlatformExecutionFence());
            var executor = new RoleBasedWorkExecutor(workspaceManager, new GitWorkspaceMutationGuard(new ProcessGitCommandRunner()), factory, new SmokePublisher(), workflow);
            const string sentence = "hello from stage 7";
            var item = new WorkItem(
                "smoke-item",
                "SMOKE-1",
                "Create one harmless text file",
                $"Create a file named hello.txt containing exactly this sentence followed by a newline: {sentence}",
                "open",
                ["agent:ready"]);

            var outcome = await executor.ExecuteAsync(new(item, 1, clock.UtcNow), CancellationToken.None);
            var workspacePath = Path.Combine(workspaceRoot, GitWorktreeWorkspaceManager.CreateKey(item.Id));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Succeeded, Is.True, outcome.FailureReason);
                Assert.That(File.Exists(Path.Combine(workspacePath, "hello.txt")), Is.True);
                Assert.That(File.ReadAllText(Path.Combine(workspacePath, "hello.txt")).TrimEnd(), Is.EqualTo(sentence));
                Assert.That(File.Exists(Path.Combine(canonical, "hello.txt")), Is.False);
            });
        }
        finally
        {
            if (root.Exists) DeleteTemporaryTree(root);
        }
    }

    private static async Task GitAsync(string workingDirectory, params string[] arguments)
    {
        var result = await new ProcessGitCommandRunner().RunAsync(workingDirectory, arguments, CancellationToken.None);
        if (result.ExitCode != 0) throw new InvalidOperationException($"Git smoke fixture setup failed: {result.StandardError}");
    }

    private static void DeleteTemporaryTree(DirectoryInfo root)
    {
        foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
        root.Delete(recursive: true);
    }

    private sealed class SmokePublisher : IWorkPublisher
    {
        public Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PublicationResult(request.Workspace.RepositoryIdentity, request.Workspace.Branch, new string('a', 40), 1, "https://example.invalid/pull/1", true));
    }
}
