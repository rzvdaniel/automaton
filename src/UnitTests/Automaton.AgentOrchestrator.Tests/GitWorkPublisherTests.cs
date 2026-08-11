using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitWorkPublisherTests
{
    private string root = null!;
    private string workspacePath = null!;
    private static readonly string BaseSha = new('a', 40);
    private static readonly string CommitSha = new('b', 40);

    [SetUp]
    public void SetUp() { root = Path.Combine(Path.GetTempPath(), "automaton-stage9-" + Guid.NewGuid().ToString("N")); workspacePath = Path.Combine(root, "work"); Directory.CreateDirectory(workspacePath); }
    [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Test]
    public async Task PublishAsync_ValidatesCommitsPushesWithoutForceAndCreatesDraft()
    {
        var git = new FakeGit(workspacePath);
        var workspaces = new FakeWorkspaces();
        var prs = new FakePullRequests();
        var result = await Create(git, workspaces, prs).PublishAsync(Request(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.CommitSha, Is.EqualTo(CommitSha));
            Assert.That(git.Commands.Any(x => x.Contains("commit") && x.Contains("user.name=Automaton Agent") && x.Contains("user.email=agent@example.test")), Is.True);
            Assert.That(git.Commands.Single(x => x.Contains("push")), Does.Not.Contain("--force"));
            Assert.That(workspaces.States.Select(x => x.State), Is.EqualTo(new[] { PublicationStates.Authorized, PublicationStates.Committed, PublicationStates.Pushed, PublicationStates.PullRequestCreated }));
            Assert.That(prs.Requests.Single().HeadSha, Is.EqualTo(CommitSha));
        });
    }

    [Test]
    public void PublishAsync_RejectsNoChangesBeforePushOrPullRequest()
    {
        var git = new FakeGit(workspacePath) { HasChanges = false };
        var prs = new FakePullRequests();
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), prs).PublishAsync(Request(), CancellationToken.None));
        Assert.Multiple(() => { Assert.That(git.Commands, Has.None.Contains("push")); Assert.That(prs.Requests, Is.Empty); });
    }

    [Test]
    public void PublishAsync_PersistsAuthorizationBeforeCommitValidationCanFail()
    {
        var git = new FakeGit(workspacePath) { HasChanges = false };
        var workspaces = new FakeWorkspaces();
        Assert.ThrowsAsync<PublicationException>(() => Create(git, workspaces, new FakePullRequests()).PublishAsync(Request(), CancellationToken.None));
        Assert.That(workspaces.States.Select(x => x.State), Is.EqualTo(new[] { PublicationStates.Authorized }));
    }

    [Test]
    public void PublishAsync_RejectsNonApprovedReviewBeforeAuthorization()
    {
        var workspaces = new FakeWorkspaces();
        var request = Request() with { Reviewer = new ReviewerHandoff(RoleResultStatuses.Blocked, "blocked", []) };
        Assert.ThrowsAsync<PublicationException>(() => Create(new FakeGit(workspacePath), workspaces, new FakePullRequests()).PublishAsync(request, CancellationToken.None));
        Assert.That(workspaces.States, Is.Empty);
    }

    [Test]
    public void PublishAsync_RejectsUnmergedIndexBeforeCommitPushOrPullRequest()
    {
        var git = new FakeGit(workspacePath) { Unmerged = true };
        var prs = new FakePullRequests();
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), prs).PublishAsync(Request(), CancellationToken.None));
        Assert.Multiple(() => { Assert.That(git.Commands, Has.None.Contains("commit")); Assert.That(git.Commands, Has.None.Contains("push")); Assert.That(prs.Requests, Is.Empty); });
    }

    [Test]
    public void PublishAsync_DeterministicValidationFailureStopsBeforeCommit()
    {
        var git = new FakeGit(workspacePath);
        var prs = new FakePullRequests();
        var error = Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), prs, ["dotnet --definitely-invalid-stage9-option"]).PublishAsync(Request(), CancellationToken.None));
        Assert.Multiple(() => { Assert.That(error!.IsRetryable, Is.False); Assert.That(git.Commands, Has.None.Contains("commit")); Assert.That(prs.Requests, Is.Empty); });
    }

    [Test]
    public void PublishAsync_RejectsWrongRemoteBeforeNetworkAccess()
    {
        var git = new FakeGit(workspacePath) { RemoteUrl = "https://attacker.example/acme/widgets.git" };
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), new FakePullRequests()).PublishAsync(Request(), CancellationToken.None));
        Assert.That(git.Commands, Has.None.Contains("ls-remote"));
    }

    [Test]
    public void PublishAsync_RejectsCanonicalCheckoutBeforeAnyGitMutation()
    {
        var git = new FakeGit(root);
        var request = Request() with { Workspace = Request().Workspace with { Path = root } };
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), new FakePullRequests()).PublishAsync(request, CancellationToken.None));
        Assert.That(git.Commands, Is.Empty);
    }

    [Test]
    public void PublishAsync_RemoteBranchAtDifferentShaFailsClosedWithoutForce()
    {
        var git = new FakeGit(workspacePath) { RemoteSha = new string('c', 40) };
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), new FakePullRequests()).PublishAsync(Request(), CancellationToken.None));
        Assert.That(git.Commands, Has.None.Contains("push"));
    }

    [Test]
    public async Task PublishAsync_AdoptsOwnCrashWindowCommitAndAlreadyPushedBranch()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        await Create(git, new FakeWorkspaces(), new FakePullRequests()).PublishAsync(Request(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(git.Commands, Has.None.Matches<string>(x => x.Contains(" commit "))); Assert.That(git.Commands, Has.None.Contains("push")); });
    }

    [TestCase(PublicationStates.Committed, false, true)]
    [TestCase(PublicationStates.Pushed, true, false)]
    public async Task RecoverAsync_ResumesDurablePartialPublicationWithoutDuplicateCommitOrPush(string state, bool remoteExists, bool expectsPush)
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = remoteExists ? CommitSha : null };
        var workspaces = new FakeWorkspaces();
        var prs = new FakePullRequests();
        var result = await Create(git, workspaces, prs).RecoverAsync(RecoveryRecord(state), Request().WorkItem, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Recognized, Is.True);
            Assert.That(git.Commands.Any(x => x.StartsWith("push ")), Is.EqualTo(expectsPush));
            Assert.That(git.Commands, Has.None.Matches<string>(x => x.Contains(" commit ")));
            Assert.That(prs.CreateRequests, Has.Count.EqualTo(1));
            Assert.That(workspaces.States.Last().State, Is.EqualTo(PublicationStates.PullRequestCreated));
        });
    }

    [Test]
    public async Task RecoverAsync_AuthorizedWithoutCommitCreatesOneDaemonCommitAndContinues()
    {
        var git = new FakeGit(workspacePath);
        var states = new FakeWorkspaces();
        var result = await Create(git, states, new FakePullRequests()).RecoverAsync(RecoveryRecord(PublicationStates.Authorized), Request().WorkItem, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Recognized, Is.True); Assert.That(git.Commands.Count(x => x.Contains("commit")), Is.EqualTo(1)); Assert.That(states.States.First().State, Is.EqualTo(PublicationStates.Committed)); });
    }

    [Test]
    public async Task RecoverAsync_AuthorizedExactCommitReconstructsMissedCommittedWrite()
    {
        var git = new FakeGit(workspacePath) { Committed = true };
        var states = new FakeWorkspaces();
        await Create(git, states, new FakePullRequests()).RecoverAsync(RecoveryRecord(PublicationStates.Authorized), Request().WorkItem, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(git.Commands, Has.None.Matches<string>(x => x.Contains(" commit "))); Assert.That(states.States.First().State, Is.EqualTo(PublicationStates.Committed)); });
    }

    [Test]
    public void RecoverAsync_UnapprovedBotLookingCommitIsNeverInspectedOrPublished()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        var prs = new FakePullRequests();
        var result = Create(git, new FakeWorkspaces(), prs).RecoverAsync(RecoveryRecord(PublicationStates.None), Request().WorkItem, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Multiple(() => { Assert.That(result.Recognized, Is.False); Assert.That(git.Commands, Is.Empty); Assert.That(prs.Requests, Is.Empty); });
    }

    [Test]
    public void RecoverAsync_AuthorizedUnexpectedCommitFailsClosed()
    {
        var git = new FakeGit(workspacePath) { Committed = true, Provenance = "Developer\0developer@example.test\0other" };
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), new FakePullRequests()).RecoverAsync(RecoveryRecord(PublicationStates.Authorized), Request().WorkItem, CancellationToken.None));
    }

    [Test]
    public async Task RecoverAsync_AuthorizedStateReconstructsMissedSidecarsFromExactRemoteAndExistingPullRequest()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        var prs = new FakePullRequests { ExistingForCreate = true };
        var result = await Create(git, new FakeWorkspaces(), prs).RecoverAsync(RecoveryRecord(PublicationStates.Authorized), Request().WorkItem, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Recognized, Is.True); Assert.That(git.Commands, Has.None.Contains("push")); Assert.That(prs.CreatedCount, Is.Zero); });
    }

    [Test]
    public async Task RecoverAsync_VerifiesPersistedPullRequestWithoutCreatingAnother()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        var prs = new FakePullRequests();
        var result = await Create(git, new FakeWorkspaces(), prs).RecoverAsync(RecoveryRecord(PublicationStates.PullRequestCreated), Request().WorkItem, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Recognized, Is.True); Assert.That(prs.VerifyRequests, Has.Count.EqualTo(1)); Assert.That(prs.CreateRequests, Is.Empty); });
    }

    [Test]
    public void RecoverAsync_MissingPersistedPullRequestFailsClosed()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        var prs = new FakePullRequests { ExactResultMissing = true };
        var workspaces = new FakeWorkspaces();
        Assert.ThrowsAsync<PublicationException>(() => Create(git, workspaces, prs).RecoverAsync(RecoveryRecord(PublicationStates.PullRequestCreated), Request().WorkItem, CancellationToken.None));
        Assert.That(workspaces.States, Is.Empty);
    }

    [Test]
    public void RecoverAsync_TransientProviderVerificationDoesNotRegressDurablePhase()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = CommitSha };
        var prs = new FakePullRequests { ExactFailure = new PublicationException("provider unavailable", true) };
        var workspaces = new FakeWorkspaces();
        var error = Assert.ThrowsAsync<PublicationException>(() => Create(git, workspaces, prs).RecoverAsync(RecoveryRecord(PublicationStates.PullRequestCreated), Request().WorkItem, CancellationToken.None));
        Assert.Multiple(() => { Assert.That(error!.IsRetryable, Is.True); Assert.That(workspaces.States, Is.Empty); });
    }

    [Test]
    public void RecoverAsync_RemoteMismatchStillFailsClosedWithoutPush()
    {
        var git = new FakeGit(workspacePath) { Committed = true, RemoteSha = new string('c', 40) };
        Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), new FakePullRequests()).RecoverAsync(RecoveryRecord(PublicationStates.Pushed), Request().WorkItem, CancellationToken.None));
        Assert.That(git.Commands, Has.None.Contains("push"));
    }

    [Test]
    public void PublishAsync_TransientPushFailureIsRetryableAndDoesNotCreatePullRequest()
    {
        var git = new FakeGit(workspacePath) { PushFailure = true };
        var prs = new FakePullRequests();
        var error = Assert.ThrowsAsync<PublicationException>(() => Create(git, new FakeWorkspaces(), prs).PublishAsync(Request(), CancellationToken.None));
        Assert.Multiple(() => { Assert.That(error!.IsRetryable, Is.True); Assert.That(prs.Requests, Is.Empty); });
    }

    [Test]
    public void BuildSubject_IsDeterministicBoundedAndSingleLine()
    {
        var item = new WorkItem("id", "acme/widgets#1", "A\r\n" + new string('x', 200), null, "open", []);
        var subject = GitWorkPublisher.BuildSubject(item);
        Assert.Multiple(() => { Assert.That(subject.Length, Is.LessThanOrEqualTo(120)); Assert.That(subject, Does.Not.Contain("\n")); Assert.That(subject, Does.StartWith("acme/widgets#1: A")); });
    }

    private GitWorkPublisher Create(FakeGit git, FakeWorkspaces workspaces, FakePullRequests prs, IReadOnlyList<string>? validation = null) => new(git, workspaces, prs, new OrchestratorOptions
    {
        Repository = new RepositoryOptions { Root = root, Identity = "acme/widgets", BaseBranch = "main" },
        Publication = new PublicationOptions { ValidationCommands = validation ?? [], GitName = "Automaton Agent", GitEmail = "agent@example.test", RemoteName = "origin", GitHubHost = "github.com" }
    });
    private PublicationRequest Request() => new(new Workspace("key", workspacePath, "automaton/work/7", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.Active, "nonce", "acme/widgets", BaseSha), new WorkItem("id", "acme/widgets#7", "Fix widget", "body", "open", []), new("READY", "plan", "steps", "tests", "risk"), new("COMPLETED", "done", "tests pass", "none"), new("APPROVED", "good", []));
    private OwnedWorkspaceRecord RecoveryRecord(string state)
    {
        var workspace = Request().Workspace with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.None };
        var publication = state is PublicationStates.None or PublicationStates.Authorized ? new PublicationRecoveryState(state) : new PublicationRecoveryState(state, CommitSha, workspace.Branch, state == PublicationStates.PullRequestCreated ? 42 : null, state == PublicationStates.PullRequestCreated ? "https://github.com/acme/widgets/pull/42" : null);
        return new(workspace, "id", new WorkspaceRepository(root, "acme/widgets"), new WorkspaceRecoveryState(WorkspaceRecoveryStates.Working), null, 3, publication);
    }

    private sealed class FakeGit(string path) : IGitCommandRunner
    {
        public bool HasChanges { get; set; } = true;
        public bool Unmerged { get; set; }
        public bool Committed { get; set; }
        public bool PushFailure { get; set; }
        public string? RemoteSha { get; set; }
        public string RemoteUrl { get; set; } = "git@github.com:acme/widgets.git";
        public string Provenance { get; set; } = "Automaton Agent\0agent@example.test\0acme/widgets#7: Fix widget";
        public List<string> Commands { get; } = [];
        public Task<GitCommandResult> RunAsync(string cwd, IReadOnlyList<string> args, CancellationToken token)
        {
            var command = string.Join(' ', args); Commands.Add(command);
            if (command == "rev-parse --show-toplevel") return Ok(path);
            if (command == "symbolic-ref --quiet --short HEAD") return Ok("automaton/work/7");
            if (command == "worktree list --porcelain") return Ok($"worktree {Path.GetFullPath(path)}\nHEAD {BaseSha}\nbranch refs/heads/automaton/work/7");
            if (command == "rev-parse refs/heads/main") return Ok(BaseSha);
            if (command == "diff --name-only --diff-filter=U") return Ok(Unmerged ? "conflicted.txt" : "");
            if (command.StartsWith("rev-parse --git-path ")) return Ok(Path.Combine(path, ".git", "absent-" + args[^1]));
            if (command == "rev-parse HEAD") return Ok(Committed ? CommitSha : BaseSha);
            if (command.StartsWith("rev-list --count ")) return Ok("1");
            if (command == "log -1 --format=%an%x00%ae%x00%s") return Ok(Provenance);
            if (command == "status --porcelain=v1 --untracked-files=all") return Ok(HasChanges ? " M file.txt" : "");
            if (command == "add --all") return Ok("");
            if (args.Contains("commit")) { Committed = true; return Ok(""); }
            if (command == "remote get-url origin") return Ok(RemoteUrl);
            if (command.StartsWith("ls-remote --heads ")) return Ok(RemoteSha is null ? "" : RemoteSha + "\trefs/heads/automaton/work/7");
            if (command.StartsWith("push ")) { if (PushFailure) return Task.FromResult(new GitCommandResult(1, "", "connection reset")); RemoteSha = CommitSha; return Ok(""); }
            throw new AssertionException("Unexpected git command: " + command);
        }
        private static Task<GitCommandResult> Ok(string output) => Task.FromResult(new GitCommandResult(0, output + "\n", ""));
    }
    private sealed class FakePullRequests : IPullRequestPublisher
    {
        public List<PullRequestPublicationRequest> Requests => CreateRequests;
        public List<PullRequestPublicationRequest> CreateRequests { get; } = [];
        public List<PullRequestPublicationRequest> VerifyRequests { get; } = [];
        public bool ExistingForCreate { get; set; }
        public bool ExactResultMissing { get; set; }
        public PublicationException? ExactFailure { get; set; }
        public int CreatedCount { get; private set; }
        public Task<PullRequestPublication> FindOrCreateAsync(PullRequestPublicationRequest request, CancellationToken token) { CreateRequests.Add(request); if (!ExistingForCreate) CreatedCount++; return Task.FromResult(Result(request, 42)); }
        public Task<PullRequestPublication?> FindExactAsync(PullRequestPublicationRequest request, int? expectedNumber, CancellationToken token)
        { VerifyRequests.Add(request); if (ExactFailure is not null) throw ExactFailure; return Task.FromResult<PullRequestPublication?>(ExactResultMissing ? null : Result(request, expectedNumber ?? 42)); }
        private static PullRequestPublication Result(PullRequestPublicationRequest request, int number) => new(number, "https://github.com/acme/widgets/pull/42", request.Repository, request.HeadBranch, request.HeadSha, request.BaseBranch, "open", true);
    }
    private sealed class FakeWorkspaces : IWorkspaceManager
    {
        public List<PublicationRecoveryState> States { get; } = [];
        public Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState state, CancellationToken token) { States.Add(state); return Task.FromResult(workspace); }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => throw new NotSupportedException();
    }
}
