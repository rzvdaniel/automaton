using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitWorktreeWorkspaceManagerTests
{
    private string root = null!;
    private string repository = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "automaton-workspace-tests", Guid.NewGuid().ToString("N"));
        repository = Path.Combine(root, "canonical");
        Directory.CreateDirectory(repository);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Test]
    public async Task CreatesOneIsolatedWorktreeAndReusesOnlyItsOwnedSidecar()
    {
        var git = new FakeGit();
        var manager = CreateManager(git);

        var first = await manager.CreateOrRecoverAsync(Request("github:4"), CancellationToken.None);
        var recovered = await manager.CreateOrRecoverAsync(Request("github:4"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Key, Does.Match("^work-[a-f0-9]{32}$"));
            Assert.That(first.Branch, Is.EqualTo($"automaton/work/{first.Key}"));
            Assert.That(recovered, Is.EqualTo(first));
            Assert.That(git.AddCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OwnershipReleaseRequiresTheExactActiveNonce()
    {
        var manager = CreateManager(new FakeGit());
        var workspace = await manager.CreateOrRecoverAsync(Request("github:5"), CancellationToken.None);
        var active = await manager.UpdateExecutionOwnershipAsync(workspace, WorkspaceExecutionOwnershipStates.Active, "nonce-a", CancellationToken.None);

        Assert.ThrowsAsync<WorkspaceOwnershipException>(() => manager.UpdateExecutionOwnershipAsync(active, WorkspaceExecutionOwnershipStates.None, "nonce-b", CancellationToken.None));
        var released = await manager.UpdateExecutionOwnershipAsync(active, WorkspaceExecutionOwnershipStates.None, "nonce-a", CancellationToken.None);

        Assert.That(released.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
    }

    [Test]
    public async Task SidecarIdentityTamperingFailsClosed()
    {
        var manager = CreateManager(new FakeGit());
        var workspace = await manager.CreateOrRecoverAsync(Request("github:6"), CancellationToken.None);
        var sidecar = Path.Combine(root, "workspaces", ".automaton-agent-orchestrator", workspace.Key + ".json");
        await File.WriteAllTextAsync(sidecar, (await File.ReadAllTextAsync(sidecar)).Replace("local-test", "other-repository", StringComparison.Ordinal));

        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.CreateOrRecoverAsync(Request("github:6"), CancellationToken.None));
    }

    private GitWorktreeWorkspaceManager CreateManager(FakeGit git) => new(Path.Combine(root, "workspaces"), git, new FixedClock());
    private WorkspaceRequest Request(string id) => new(new WorkItem(id, id, "Task", null, "open", []), new WorkspaceRepository(repository, "local-test", "main"));
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }
    private sealed class FakeGit : IGitCommandRunner
    {
        private readonly Dictionary<string, string> worktrees = new(StringComparer.OrdinalIgnoreCase);
        public int AddCount { get; private set; }
        public Task<GitCommandResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token)
        {
            if (arguments.SequenceEqual(["rev-parse", "refs/heads/main"])) return Task.FromResult(new GitCommandResult(0, new string('a', 40), string.Empty));
            if (arguments.SequenceEqual(["worktree", "list", "--porcelain"]))
                return Task.FromResult(new GitCommandResult(0, string.Join("\n\n", worktrees.Select(pair => $"worktree {pair.Key}\nHEAD deadbeef\nbranch refs/heads/{pair.Value}")), string.Empty));
            if (arguments.Take(3).SequenceEqual(["show-ref", "--verify", "--quiet"])) return Task.FromResult(new GitCommandResult(1, string.Empty, string.Empty));
            if (arguments.Take(3).SequenceEqual(["worktree", "add", "-b"]))
            {
                var branch = arguments[3];
                var path = arguments[4];
                worktrees[Path.GetFullPath(path)] = branch;
                Directory.CreateDirectory(path);
                AddCount++;
                return Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty));
            }
            throw new AssertionException("Unexpected Git command: " + string.Join(' ', arguments));
        }
    }
}
