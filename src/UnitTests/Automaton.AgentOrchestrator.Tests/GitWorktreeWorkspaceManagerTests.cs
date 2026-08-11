using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitWorktreeWorkspaceManagerTests
{
    private string testRoot = null!;
    private string repositoryRoot = null!;

    [SetUp]
    public void SetUp()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "automaton-workspace-tests", Guid.NewGuid().ToString("N"));
        repositoryRoot = Path.Combine(testRoot, "canonical");
        Directory.CreateDirectory(repositoryRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
    }

    [Test]
    public void CreateKey_IsDeterministicCollisionResistantAndPathSafe()
    {
        var first = GitWorktreeWorkspaceManager.CreateKey("github-issue:acme/widgets:4");
        var same = GitWorktreeWorkspaceManager.CreateKey("github-issue:acme/widgets:4");
        var different = GitWorktreeWorkspaceManager.CreateKey("../../tracker-controlled-value");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first, Is.Not.EqualTo(different));
            Assert.That(first, Does.Match("^work-[a-f0-9]{32}$"));
            Assert.That(different, Does.Not.Contain(".."));
        });
    }

    [Test]
    public async Task CreateOrRecoverAsync_CreatesDedicatedWorktreeBranchAndSidecar()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);

        var workspace = await manager.CreateOrRecoverAsync(Request("github-issue:acme/widgets:4"), CancellationToken.None);

        var sidecar = Path.Combine(testRoot, "workspaces", ".automaton-agent-orchestrator", workspace.Key + ".json");
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(workspace.Path), Is.True);
            Assert.That(workspace.Branch, Is.EqualTo($"automaton/work/{workspace.Key}"));
            Assert.That(workspace.LifecycleStatus, Is.EqualTo(WorkspaceLifecycleStates.Ready));
            Assert.That(workspace.LaunchNonce, Has.Length.EqualTo(32));
            Assert.That(File.ReadAllText(sidecar), Does.Contain("github-issue:acme/widgets:4"));
            Assert.That(git.Commands.Single(command => command.Arguments.Contains("worktree") && command.Arguments.Contains("add")).WorkingDirectory, Is.EqualTo(repositoryRoot));
        });
    }

    [Test]
    public async Task CreateOrRecoverAsync_ReusesRegisteredDaemonOwnedWorkspace()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var first = await manager.CreateOrRecoverAsync(Request("work-4"), CancellationToken.None);
        var commandCount = git.Commands.Count;

        var recovered = await manager.CreateOrRecoverAsync(Request("work-4"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recovered, Is.EqualTo(first));
            Assert.That(git.Commands.Count, Is.EqualTo(commandCount + 1));
            Assert.That(git.Commands.Last().Arguments, Is.EqualTo(new[] { "worktree", "list", "--porcelain" }));
        });
    }

    [Test]
    public async Task CreateOrRecoverAsync_RefusesUnownedOrActiveWorkspaceState()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var request = Request("work-5");
        var key = GitWorktreeWorkspaceManager.CreateKey(request.WorkItem.Id);
        Directory.CreateDirectory(Path.Combine(testRoot, "workspaces", key));

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));

        Directory.Delete(Path.Combine(testRoot, "workspaces", key));
        var workspace = await manager.CreateOrRecoverAsync(request, CancellationToken.None);
        await manager.UpdateLifecycleAsync(workspace, WorkspaceLifecycleStates.Active, CancellationToken.None);

        Assert.ThrowsAsync<WorkspaceOwnershipException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));
    }

    [Test]
    public async Task CreateOrRecoverAsync_RecoversMissingWorktreeUsingExistingBranch()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var workspace = await manager.CreateOrRecoverAsync(Request("work-6"), CancellationToken.None);
        Directory.Delete(workspace.Path, true);
        git.Worktrees.Remove(Path.GetFullPath(workspace.Path));

        var recovered = await manager.CreateOrRecoverAsync(Request("work-6"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recovered.Path, Is.EqualTo(workspace.Path));
            Assert.That(git.Commands.Last(command => command.Arguments.Contains("add")).Arguments, Does.Not.Contain("-b"));
            Assert.That(Directory.Exists(recovered.Path), Is.True);
        });
    }

    [Test]
    public async Task CreateOrRecoverAsync_RejectsTamperedSidecarState()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var request = Request("work-sidecar");
        var workspace = await manager.CreateOrRecoverAsync(request, CancellationToken.None);
        var sidecar = Path.Combine(testRoot, "workspaces", ".automaton-agent-orchestrator", workspace.Key + ".json");
        await File.WriteAllTextAsync(sidecar, (await File.ReadAllTextAsync(sidecar)).Replace("local-test", "unexpected-repository", StringComparison.Ordinal));

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));
        Assert.That(Directory.Exists(workspace.Path), Is.True);
    }

    [Test]
    public async Task CleanupTerminalAsync_OnlyRemovesVerifiedTerminalWorktree()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var workspace = await manager.CreateOrRecoverAsync(Request("work-7"), CancellationToken.None);

        await manager.UpdateLifecycleAsync(workspace, WorkspaceLifecycleStates.RetryableFailure, CancellationToken.None);
        Assert.ThrowsAsync<WorkspaceException>(async () => await manager.CleanupTerminalAsync(workspace, CancellationToken.None));
        Assert.That(Directory.Exists(workspace.Path), Is.True);

        var terminal = await manager.UpdateLifecycleAsync(workspace, WorkspaceLifecycleStates.Terminal, CancellationToken.None);
        await manager.CleanupTerminalAsync(terminal, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(workspace.Path), Is.False);
            Assert.That(git.Commands.Last().Arguments, Is.EqualTo(new[] { "worktree", "remove", "--force", workspace.Path }));
        });
    }

    [Test]
    public async Task CleanupTerminalAsync_RefusesPathOutsideConfiguredRoot()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("work-8"), CancellationToken.None);
        var terminal = await manager.UpdateLifecycleAsync(workspace, WorkspaceLifecycleStates.Terminal, CancellationToken.None);
        var outside = terminal with { Path = Path.Combine(testRoot, "outside") };

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CleanupTerminalAsync(outside, CancellationToken.None));
    }

    [Test]
    public void CreateOrRecoverAsync_RejectsCanonicalRepositoryInsideWorkspaceRoot()
    {
        var manager = new GitWorktreeWorkspaceManager(Path.Combine(testRoot, "workspaces"), new FakeGitCommandRunner(), new FixedClock());
        var request = Request("work-9") with { Repository = new WorkspaceRepository(Path.Combine(testRoot, "workspaces", "canonical"), "local-test") };
        Directory.CreateDirectory(request.Repository.RootPath);

        Assert.ThrowsAsync<WorkspaceException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));
    }

    private GitWorktreeWorkspaceManager CreateManager(FakeGitCommandRunner git)
        => new(Path.Combine(testRoot, "workspaces"), git, new FixedClock());

    private WorkspaceRequest Request(string id)
        => new(new WorkItem(id, "test-1", "Title", null, "open", []), new WorkspaceRepository(repositoryRoot, "local-test"));

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> Commands { get; } = [];
        public HashSet<string> Worktrees { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> branches = new(StringComparer.Ordinal);

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add((workingDirectory, arguments));
            if (arguments.SequenceEqual(new[] { "worktree", "list", "--porcelain" }))
                return Task.FromResult(new GitCommandResult(0, string.Join("\n", Worktrees.Select(path => "worktree " + path)), string.Empty));
            if (arguments.Take(3).SequenceEqual(new[] { "show-ref", "--verify", "--quiet" }))
                return Task.FromResult(new GitCommandResult(branches.Contains(arguments[3]["refs/heads/".Length..]) ? 0 : 1, string.Empty, string.Empty));
            if (arguments.Take(2).SequenceEqual(new[] { "worktree", "add" }))
            {
                var branchIndex = arguments.ToList().IndexOf("-b");
                var path = branchIndex >= 0 ? arguments[branchIndex + 2] : arguments[3];
                var branch = branchIndex >= 0 ? arguments[branchIndex + 1] : arguments[4];
                branches.Add(branch);
                Worktrees.Add(Path.GetFullPath(path));
                Directory.CreateDirectory(path);
                return Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty));
            }
            if (arguments.Take(3).SequenceEqual(new[] { "worktree", "remove", "--force" }))
            {
                Worktrees.Remove(Path.GetFullPath(arguments[3]));
                if (Directory.Exists(arguments[3])) Directory.Delete(arguments[3], true);
                return Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty));
            }
            throw new AssertionException("Unexpected Git command.");
        }
    }
}
