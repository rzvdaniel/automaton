using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class BlockedRunRecoveryOperatorTests
{
    [Test]
    public async Task ExplicitRetryTransitionsAuthorizedBlockedRunOnce()
    {
        var fixture = Create(PublicationStates.Authorized);
        var result = await fixture.Service.RequestRetryAsync("issue-4", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Attempt, Is.EqualTo(3));
            Assert.That(fixture.Workspaces.Updated?.State, Is.EqualTo(WorkspaceRecoveryStates.Working));
            Assert.That(fixture.Telemetry.Events, Has.One.Matches<string>(x => x == "run.operator_retry_requested"));
        });
    }

    [Test]
    public async Task MissingPublicationAuthorizationIsRejected()
    {
        var fixture = Create(PublicationStates.None);
        var result = await fixture.Service.RequestRetryAsync("issue-4", CancellationToken.None);
        Assert.That(result.Category, Is.EqualTo("missing-publication-authorization"));
        Assert.That(fixture.Workspaces.Updated, Is.Null);
    }

    [Test]
    public async Task QuarantinedOwnershipIsRejected()
    {
        var fixture = Create(PublicationStates.Authorized, WorkspaceExecutionOwnershipStates.Quarantined);
        var result = await fixture.Service.RequestRetryAsync("issue-4", CancellationToken.None);
        Assert.That(result.Category, Is.EqualTo("quarantined"));
        Assert.That(fixture.Workspaces.Updated, Is.Null);
    }

    [Test]
    public async Task StateTransitionConflictDoesNotAuthorizeRetry()
    {
        var fixture = Create(PublicationStates.Authorized);
        fixture.Workspaces.Conflict = true;
        var result = await fixture.Service.RequestRetryAsync("issue-4", CancellationToken.None);
        Assert.That(result.Category, Is.EqualTo("transition-conflict"));
    }

    private static Fixture Create(string publicationState, string ownership = WorkspaceExecutionOwnershipStates.None)
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-retry-test-" + Guid.NewGuid().ToString("N"));
        var workspace = new Workspace("work-key", Path.Combine(root, "work-key"), "automaton/work/work-key", WorkspaceLifecycleStates.Ready, ownership, "nonce", "acme/widgets", new string('b', 40));
        var record = new OwnedWorkspaceRecord(workspace, "issue-4", new(root, "acme/widgets", "main"), new(WorkspaceRecoveryStates.Blocked, 2, 3), null, 6,
            new PublicationRecoveryState(publicationState, publicationState == PublicationStates.Authorized ? null : new string('a', 40), workspace.Branch, AuthorizedParentSha: workspace.BaseCommitSha));
        var workspaces = new FakeWorkspaces(record);
        var options = new OrchestratorOptions { Workspace = new WorkspaceOptions { Root = root }, Repository = new RepositoryOptions { Root = Path.Combine(root, "canonical"), Identity = "acme/widgets", BaseBranch = "main" }, Tracker = new TrackerOptions { ActiveProviderLifecycleStates = ["open"], RequiredLabels = ["agent:ready"] } };
        var telemetry = new FakeTelemetry();
        return new(new BlockedRunRecoveryOperator(workspaces, new FakeGit(workspace), new FakeTracker(), options, telemetry), workspaces, telemetry);
    }

    private sealed record Fixture(BlockedRunRecoveryOperator Service, FakeWorkspaces Workspaces, FakeTelemetry Telemetry);
    private sealed class FakeTracker : IWorkTracker
    {
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<WorkItem>>([]);
        public Task<WorkItem?> GetByIdAsync(string id, CancellationToken token) => Task.FromResult<WorkItem?>(new(id, id, "title", null, "open", ["agent:ready"]));
    }
    private sealed class FakeGit(Workspace workspace) : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(string cwd, IReadOnlyList<string> args, CancellationToken token) => Task.FromResult(new GitCommandResult(0, $"worktree {workspace.Path}\nHEAD abc\nbranch refs/heads/{workspace.Branch}\n", ""));
    }
    private sealed class FakeTelemetry : IOperatorTelemetry
    {
        public List<string> Events { get; } = [];
        public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null) => Events.Add(eventType);
        public OperatorEvent? LatestFor(string workItemId) => null;
        public void SetDaemonState(string state, string? error = null) { }
        public (string State, string? LastError) DaemonState() => ("stopped", null);
    }
    private sealed class FakeWorkspaces(OwnedWorkspaceRecord record) : IWorkspaceManager
    {
        public WorkspaceRecoveryState? Updated { get; private set; }
        public bool Conflict { get; set; }
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([record]);
        public Task<Workspace> TransitionRecoveryStateAsync(Workspace workspace, string expectedState, string? expectedPublicationState, WorkspaceRecoveryState state, CancellationToken token) { if (Conflict) throw new WorkspaceRecoveryException("conflict"); Updated = state; return Task.FromResult(workspace); }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState state, CancellationToken token) => throw new NotSupportedException(); public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken cancellationToken, bool unused = false) => throw new NotSupportedException(); public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => throw new NotSupportedException();
    }
}
