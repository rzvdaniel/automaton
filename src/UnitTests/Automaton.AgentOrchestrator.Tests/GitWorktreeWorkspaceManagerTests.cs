using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;
using System.Text.Json.Nodes;

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
            Assert.That(workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
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
    public async Task CreateOrRecoverAsync_RefusesActiveOwnershipUntilItIsReleasedWithoutChangingLifecycle()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var request = Request("work-5");
        var key = GitWorktreeWorkspaceManager.CreateKey(request.WorkItem.Id);
        Directory.CreateDirectory(Path.Combine(testRoot, "workspaces", key));

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));

        Directory.Delete(Path.Combine(testRoot, "workspaces", key));
        var workspace = await manager.CreateOrRecoverAsync(request, CancellationToken.None);
        var active = await manager.UpdateExecutionOwnershipAsync(workspace, WorkspaceExecutionOwnershipStates.Active, "active-nonce", CancellationToken.None);
        var retryable = await manager.UpdateLifecycleAsync(active, WorkspaceLifecycleStates.RetryableFailure, CancellationToken.None);

        Assert.ThrowsAsync<WorkspaceOwnershipException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));
        Assert.ThrowsAsync<WorkspaceOwnershipException>(async () => await manager.UpdateExecutionOwnershipAsync(retryable, WorkspaceExecutionOwnershipStates.None, "different-nonce", CancellationToken.None));

        var released = await manager.UpdateExecutionOwnershipAsync(retryable, WorkspaceExecutionOwnershipStates.None, retryable.LaunchNonce, CancellationToken.None);
        var recovered = await manager.CreateOrRecoverAsync(request, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(released.LifecycleStatus, Is.EqualTo(WorkspaceLifecycleStates.RetryableFailure));
            Assert.That(released.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
            Assert.That(recovered.Path, Is.EqualTo(workspace.Path));
        });
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
    public async Task CreateOrRecoverAsync_RejectsRegisteredWorktreeOnWrongBranch()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var workspace = await manager.CreateOrRecoverAsync(Request("work-wrong-reuse"), CancellationToken.None);
        git.Worktrees[Path.GetFullPath(workspace.Path)] = "other-branch";

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CreateOrRecoverAsync(Request("work-wrong-reuse"), CancellationToken.None));
    }

    [Test]
    public async Task CleanupTerminalAsync_RejectsRegisteredWorktreeOnWrongBranch()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var workspace = await manager.CreateOrRecoverAsync(Request("work-wrong-cleanup"), CancellationToken.None);
        var terminal = await manager.UpdateLifecycleAsync(workspace, WorkspaceLifecycleStates.Terminal, CancellationToken.None);
        git.Worktrees[Path.GetFullPath(workspace.Path)] = "other-branch";

        Assert.ThrowsAsync<WorkspaceRecoveryException>(async () => await manager.CleanupTerminalAsync(terminal, CancellationToken.None));
        Assert.That(Directory.Exists(workspace.Path), Is.True);
    }

    [Test]
    public void CreateOrRecoverAsync_RejectsCanonicalRepositoryInsideWorkspaceRoot()
    {
        var manager = new GitWorktreeWorkspaceManager(Path.Combine(testRoot, "workspaces"), new FakeGitCommandRunner(), new FixedClock());
        var request = Request("work-9") with { Repository = new WorkspaceRepository(Path.Combine(testRoot, "workspaces", "canonical"), "local-test") };
        Directory.CreateDirectory(request.Repository.RootPath);

        Assert.ThrowsAsync<WorkspaceException>(async () => await manager.CreateOrRecoverAsync(request, CancellationToken.None));
    }

    [Test]
    public async Task CreateAsync_UsesConfiguredBaseBranchCommitRatherThanCanonicalHead()
    {
        var git = new FakeGitCommandRunner();
        var manager = CreateManager(git);
        var request = Request("base-branch") with { Repository = new WorkspaceRepository(repositoryRoot, "local-test", "trunk") };
        await manager.CreateOrRecoverAsync(request, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(git.Commands, Has.Some.Matches<(string, IReadOnlyList<string>)>(x => x.Item2.SequenceEqual(new[] { "rev-parse", "refs/heads/trunk" })));
            Assert.That(git.Commands, Has.Some.Matches<(string, IReadOnlyList<string>)>(x => x.Item2.Count >= 6 && x.Item2.Take(3).SequenceEqual(new[] { "worktree", "add", "-b" }) && x.Item2[^1] == new string('a', 40)));
        });
    }

    [Test]
    public async Task EnumerateOwnedWorkspacesAsync_RestoresDurableAttemptAndExecutionIdentity()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("recovery-work"), CancellationToken.None);
        workspace = await manager.UpdateRecoveryStateAsync(workspace,
            new WorkspaceRecoveryState(WorkspaceRecoveryStates.RetryableFailure, 2, 3, DateTimeOffset.Parse("2026-01-02T00:00:00Z"), "temporary"), CancellationToken.None);
        workspace = await manager.UpdateExecutionOwnershipAsync(workspace, WorkspaceExecutionOwnershipStates.Active, "launch-2", CancellationToken.None);
        await manager.RecordExecutionIdentityAsync(workspace,
            new DurableExecutionIdentity(456, DateTimeOffset.Parse("2026-01-01T01:00:00Z"), "launch-2", ExecutionFencingKinds.WindowsJob), CancellationToken.None);

        var records = await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None);
        var record = records.Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.SchemaVersion, Is.EqualTo(5));
            Assert.That(record.Recovery.State, Is.EqualTo(WorkspaceRecoveryStates.RetryableFailure));
            Assert.That(record.Recovery.LastCompletedAttempt, Is.EqualTo(2));
            Assert.That(record.Recovery.NextAttempt, Is.EqualTo(3));
            Assert.That(record.ExecutionIdentity?.ProcessId, Is.EqualTo(456));
            Assert.That(record.ExecutionIdentity?.LaunchNonce, Is.EqualTo("launch-2"));
        });
    }

    [Test]
    public async Task PublicationRecoveryState_RoundTripsBoundedStageMetadata()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("published-work"), CancellationToken.None);
        var sha = new string('b', 40);
        workspace = await manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Authorized, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Committed, sha, workspace.Branch, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Pushed, sha, workspace.Branch, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        await manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.PullRequestCreated, sha, workspace.Branch, 42, "https://github.com/acme/widgets/pull/42", 1, workspace.BaseCommitSha), CancellationToken.None);

        var record = (await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.Workspace.BaseCommitSha, Is.EqualTo(new string('a', 40)));
            Assert.That(record.Publication!.State, Is.EqualTo(PublicationStates.PullRequestCreated));
            Assert.That(record.Publication.CommitSha, Is.EqualTo(sha));
            Assert.That(record.Publication.PullRequestNumber, Is.EqualTo(42));
        });
    }

    [Test]
    public async Task UpdatePublicationStateAsync_RejectsIncompletePullRequestIdentity()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("invalid-publication"), CancellationToken.None);
        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.PullRequestCreated, new string('b', 40), workspace.Branch), CancellationToken.None));
    }

    [Test]
    public async Task PublicationStateCannotSkipHostAuthorization()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("unauthorized-publication"), CancellationToken.None);
        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Committed, new string('b', 40), workspace.Branch), CancellationToken.None));
    }

    [Test]
    public async Task SchemaThreeCommittedPublicationMigratesAsHostAuthorizedHistory()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("schema-three-publication"), CancellationToken.None);
        var sidecarPath = SidecarPath(workspace);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(sidecarPath))!.AsObject();
        json["SchemaVersion"] = 3;
        json["PublicationState"] = PublicationStates.Committed;
        json["PublicationCommitSha"] = new string('b', 40);
        json["PublicationRemoteBranch"] = workspace.Branch;
        await File.WriteAllTextAsync(sidecarPath, json.ToJsonString());

        var record = (await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None)).Single();
        Assert.Multiple(() => { Assert.That(record.SchemaVersion, Is.EqualTo(5)); Assert.That(record.Publication!.State, Is.EqualTo(PublicationStates.Committed)); });
    }

    [Test]
    public async Task SchemaThreeCannotManufactureNewAuthorizedState()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("schema-three-forged-authorization"), CancellationToken.None);
        var sidecarPath = SidecarPath(workspace);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(sidecarPath))!.AsObject();
        json["SchemaVersion"] = 3;
        json["PublicationState"] = PublicationStates.Authorized;
        await File.WriteAllTextAsync(sidecarPath, json.ToJsonString());

        var record = (await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None)).Single();
        Assert.That(record.Publication!.State, Is.EqualTo(PublicationStates.None));
    }

    [Test]
    public async Task CiStateBindsToExactPublishedHeadAndNewRevisionClearsStaleObservation()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("ci-revision"), CancellationToken.None);
        var first = new string('b', 40);
        workspace = await manager.UpdatePublicationStateAsync(workspace, new(PublicationStates.Authorized, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdatePublicationStateAsync(workspace, new(PublicationStates.Committed, first, workspace.Branch, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdatePublicationStateAsync(workspace, new(PublicationStates.Pushed, first, workspace.Branch, Revision: 1, AuthorizedParentSha: workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdatePublicationStateAsync(workspace, new(PublicationStates.PullRequestCreated, first, workspace.Branch, 42, "https://github.test/pull/42", 1, workspace.BaseCommitSha), CancellationToken.None);
        workspace = await manager.UpdateCiStateAsync(workspace, new(CiStatuses.Pending, first, 0, DateTimeOffset.Parse("2026-01-01T00:00:00Z")), CancellationToken.None);

        workspace = await manager.UpdatePublicationStateAsync(workspace, new(PublicationStates.Authorized, RemoteBranch: workspace.Branch, PullRequestNumber: 42,
            PullRequestUrl: "https://github.test/pull/42", Revision: 2, AuthorizedParentSha: first), CancellationToken.None);
        var record = (await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None)).Single();
        Assert.Multiple(() => { Assert.That(record.Publication!.State, Is.EqualTo(PublicationStates.Authorized)); Assert.That(record.Publication.Revision, Is.EqualTo(2)); Assert.That(record.Publication.AuthorizedParentSha, Is.EqualTo(first)); Assert.That(record.Ci, Is.Null); });
    }

    [Test]
    public async Task CiStateRejectsStaleHeadSha()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("stale-ci"), CancellationToken.None);
        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.UpdateCiStateAsync(workspace, new(CiStatuses.Passed, new string('c', 40), 0), CancellationToken.None));
    }

    [Test]
    public async Task EnumerateOwnedWorkspacesAsync_RejectsSidecarBranchTampering()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("tampered-branch"), CancellationToken.None);
        var sidecarPath = SidecarPath(workspace);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(sidecarPath))!.AsObject();
        json["Branch"] = "automaton/work/other";
        await File.WriteAllTextAsync(sidecarPath, json.ToJsonString());

        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None));
    }

    [Test]
    public async Task EnumerateOwnedWorkspacesAsync_CorruptSidecarFailsClosed()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("corrupt-sidecar"), CancellationToken.None);
        await File.WriteAllTextAsync(SidecarPath(workspace), "{not-json");

        Assert.ThrowsAsync<WorkspaceRecoveryException>(() => manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None));
    }

    [Test]
    public async Task EnumerateOwnedWorkspacesAsync_MigratesLegacySidecarConservatively()
    {
        var manager = CreateManager(new FakeGitCommandRunner());
        var workspace = await manager.CreateOrRecoverAsync(Request("legacy-sidecar"), CancellationToken.None);
        var sidecarPath = SidecarPath(workspace);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(sidecarPath))!.AsObject();
        json.Remove("SchemaVersion");
        json.Remove("SchedulerState");
        await File.WriteAllTextAsync(sidecarPath, json.ToJsonString());

        var record = (await manager.EnumerateOwnedWorkspacesAsync(Request("unused").Repository, CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(record.SchemaVersion, Is.EqualTo(5));
            Assert.That(record.Recovery.State, Is.EqualTo(WorkspaceRecoveryStates.Blocked));
            Assert.That(record.Recovery.Reason, Does.Contain("Legacy"));
        });
    }

    private string SidecarPath(Workspace workspace)
        => Path.Combine(testRoot, "workspaces", ".automaton-agent-orchestrator", workspace.Key + ".json");

    private GitWorktreeWorkspaceManager CreateManager(FakeGitCommandRunner git)
        => new(Path.Combine(testRoot, "workspaces"), git, new FixedClock());

    private WorkspaceRequest Request(string id)
        => new(new WorkItem(id, "test-1", "Title", null, "open", []), new WorkspaceRepository(repositoryRoot, "local-test"));

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> Commands { get; } = [];
        public Dictionary<string, string> Worktrees { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> branches = new(StringComparer.Ordinal);

        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add((workingDirectory, arguments));
            if (arguments.Count == 2 && arguments[0] == "rev-parse" && arguments[1].StartsWith("refs/heads/", StringComparison.Ordinal))
                return Task.FromResult(new GitCommandResult(0, new string('a', 40) + "\n", string.Empty));
            if (arguments.SequenceEqual(new[] { "worktree", "list", "--porcelain" }))
                return Task.FromResult(new GitCommandResult(0, string.Join("\n\n", Worktrees.Select(worktree => $"worktree {worktree.Key}\nHEAD deadbeef\nbranch refs/heads/{worktree.Value}")), string.Empty));
            if (arguments.Take(3).SequenceEqual(new[] { "show-ref", "--verify", "--quiet" }))
                return Task.FromResult(new GitCommandResult(branches.Contains(arguments[3]["refs/heads/".Length..]) ? 0 : 1, string.Empty, string.Empty));
            if (arguments.Take(2).SequenceEqual(new[] { "worktree", "add" }))
            {
                var branchIndex = arguments.ToList().IndexOf("-b");
                var path = branchIndex >= 0 ? arguments[branchIndex + 2] : arguments[3];
                var branch = branchIndex >= 0 ? arguments[branchIndex + 1] : arguments[4];
                branches.Add(branch);
                Worktrees[Path.GetFullPath(path)] = branch;
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
