using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Ci;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.ReviewFeedback;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class ReviewFeedbackShepherdTests
{
    private static readonly string Sha1 = new('a', 40);
    private static readonly string Sha2 = new('b', 40);
    private static readonly ReviewFeedbackTarget Target = new("acme/widgets", 42, "automaton/work/7", Sha1);

    [Test]
    public async Task AwaitingHumanDoesNotLaunchAgentThenApprovalCompletes()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.AwaitingHuman), Snapshot(Sha1, ReviewFeedbackStatuses.Approved)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(fixture.Remediator.Requests, Is.Empty); Assert.That(fixture.Delay.Count, Is.EqualTo(1)); });
    }

    [Test]
    public async Task ActionableFeedbackRequiresReviewPublicationCiAndNewHeadApproval()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable), Snapshot(Sha2, ReviewFeedbackStatuses.Approved)], [Publication(Sha2)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(result.HeadSha, Is.EqualTo(Sha2)); Assert.That(fixture.Remediator.Requests, Has.Count.EqualTo(1)); Assert.That(fixture.Ci.Targets.Single().HeadSha, Is.EqualTo(Sha2)); });
    }

    [Test]
    public async Task RoundLimitFailsClosedWithoutAnotherAgent()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable)], maxRounds: 0);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.False); Assert.That(fixture.Remediator.Requests, Is.Empty); });
    }

    [Test]
    public async Task RepublishedRevisionMustPassCiBeforeNextFeedbackObservation()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable)], [Publication(Sha2)], ciSuccess: false);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.False); Assert.That(fixture.Provider.Targets, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task CiProviderFailureAtPostPublicationBoundaryBecomesCiCategory()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable)], [Publication(Sha2)]);
        fixture.Ci.Failure = new CiProviderException("provider payload must not leak", true);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.FailureCategory, Is.EqualTo(ReviewFeedbackFailureCategories.Ci)); Assert.That(result.IsRetryable, Is.True); Assert.That(result.FailureReason, Is.EqualTo("human feedback CI failed")); });
    }

    [Test]
    public async Task RemediationFailurePreservesBoundedDeveloperCategory()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable)]);
        fixture.Remediator.Failure = new ReviewFeedbackRemediationException(ReviewFeedbackFailureCategories.Developer, "human feedback Developer remediation failed");
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.False); Assert.That(result.FailureCategory, Is.EqualTo(ReviewFeedbackFailureCategories.Developer)); Assert.That(result.FailureReason, Is.EqualTo("human feedback Developer remediation failed")); });
    }

    [Test]
    public async Task ActionableBatchIsPersistedPendingBeforeRemediationAndConsumedAtFinalHead()
    {
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable), Snapshot(Sha2, ReviewFeedbackStatuses.Approved)], [Publication(Sha2)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(fixture.Workspaces.States.Any(x => x.Pending?.Count == 1), Is.True);
        var final = fixture.Workspaces.States.Last();
        Assert.That(final.Consumed, Is.Not.Null);
        Assert.That(final.Consumed!.Single().SatisfiedHeadSha, Is.EqualTo(Sha2));
        Assert.That(final.Consumed.Single().SatisfiedRevision, Is.EqualTo(2));
        Assert.That(final.Pending, Is.Empty);
    }

    [Test]
    public async Task RemediationPropagatesAuthoritativeReleasedWorkspaceToCiAndPersistence()
    {
        var stale = Workspace("nonce-a");
        var released = Workspace("nonce-b");
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable), Snapshot(Sha2, ReviewFeedbackStatuses.Approved)], [Publication(Sha2)], workspace: stale, remediationWorkspace: released);
        var result = await fixture.Shepherd.ShepherdAsync(Target, stale, Work(), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(fixture.Ci.Workspaces.Single().LaunchNonce, Is.EqualTo("nonce-b"));
        Assert.That(fixture.Workspaces.StateWorkspaces.Last().LaunchNonce, Is.EqualTo("nonce-b"));
    }

    [Test]
    public async Task ReviewedNoOpUsesAuthoritativeReleasedWorkspaceAndSkipsCi()
    {
        var stale = Workspace("nonce-a");
        var released = Workspace("nonce-b");
        var noOp = Publication(Sha1) with { IsReviewedNoOp = true };
        var fixture = Create([Snapshot(Sha1, ReviewFeedbackStatuses.Actionable)], [noOp], workspace: stale, remediationWorkspace: released);
        var result = await fixture.Shepherd.ShepherdAsync(Target, stale, Work(), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True);
        Assert.That(fixture.Ci.Workspaces, Is.Empty);
        Assert.That(fixture.Workspaces.StateWorkspaces.Last().LaunchNonce, Is.EqualTo("nonce-b"));
    }

    private static Fixture Create(IReadOnlyList<ReviewFeedbackSnapshot> snapshots, IReadOnlyList<PublicationResult>? publications = null, int maxRounds = 2, bool ciSuccess = true, Workspace? workspace = null, Workspace? remediationWorkspace = null)
    {
        var provider = new FakeProvider(snapshots); var remediator = new FakeRemediator(publications ?? [], remediationWorkspace); var ci = new FakeCi(ciSuccess); var delay = new FakeDelay();
        var options = new OrchestratorOptions { ReviewFeedback = new ReviewFeedbackOptions { Enabled = true, PollIntervalMilliseconds = 1, MaxRemediationRounds = maxRounds } };
        var workspaces = new FakeWorkspaces();
        return new(new(provider, remediator, ci, workspaces, new FakeClock(), delay, options), provider, remediator, ci, delay, workspaces);
    }
    private static ReviewFeedbackSnapshot Snapshot(string sha, string status) => new(sha, status, status == ReviewFeedbackStatuses.Actionable ? [new("1", ReviewFeedbackKinds.ReviewComment, "alice", "fix")] : []);
    private static PublicationResult Publication(string sha) => new("acme/widgets", "automaton/work/7", sha, 42, "url", true);
    private static Workspace Workspace(string nonce = "nonce") => new("key", "C:/workspace", "automaton/work/7", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, nonce, "acme/widgets");
    private static WorkItem Work() => new("id", "#7", "Fix", null, "open", ["agent:ready"]);
    private sealed record Fixture(ReviewFeedbackShepherd Shepherd, FakeProvider Provider, FakeRemediator Remediator, FakeCi Ci, FakeDelay Delay, FakeWorkspaces Workspaces);
    private sealed class FakeProvider(IEnumerable<ReviewFeedbackSnapshot> values) : IReviewFeedbackProvider { private readonly Queue<ReviewFeedbackSnapshot> values = new(values); public List<ReviewFeedbackTarget> Targets { get; } = []; public Task<ReviewFeedbackSnapshot> GetAsync(ReviewFeedbackTarget target, CancellationToken token) { Targets.Add(target); return Task.FromResult(values.Dequeue()); } }
    private sealed class FakeRemediator(IEnumerable<PublicationResult> values, Workspace? released) : IReviewFeedbackRemediator { private readonly Queue<PublicationResult> values = new(values); private readonly Workspace? released = released; public List<ReviewFeedbackRemediationRequest> Requests { get; } = []; public ReviewFeedbackRemediationException? Failure { get; set; } public Task<ReviewFeedbackRemediationResult> RemediateAsync(ReviewFeedbackRemediationRequest request, CancellationToken token) { Requests.Add(request); if (Failure is not null) throw Failure; return Task.FromResult(new ReviewFeedbackRemediationResult(values.Dequeue(), released ?? request.Workspace)); } }
    private sealed class FakeCi(bool success) : ICiShepherd { public List<CiTarget> Targets { get; } = []; public List<Workspace> Workspaces { get; } = []; public CiProviderException? Failure { get; set; } public Task<CiShepherdResult> ShepherdAsync(CiTarget target, Workspace workspace, WorkItem item, CancellationToken token) { Targets.Add(target); Workspaces.Add(workspace); if (Failure is not null) throw Failure; return Task.FromResult(success ? CiShepherdResult.Success(target.HeadSha, 0) : CiShepherdResult.Blocked("failed", target.HeadSha, 0)); } }
    private sealed class FakeDelay : IReviewFeedbackPollDelay { public int Count { get; private set; } public Task DelayAsync(TimeSpan delay, CancellationToken token) { Count++; return Task.CompletedTask; } }
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }
    private sealed class FakeWorkspaces : IWorkspaceManager
    {
        public List<ReviewFeedbackRecoveryState> States { get; } = [];
        public List<Workspace> StateWorkspaces { get; } = [];
        public Task<Workspace> UpdateReviewFeedbackStateAsync(Workspace workspace, ReviewFeedbackRecoveryState state, CancellationToken token)
        {
            var prior = States.LastOrDefault();
            var preserved = state with { Consumed = state.Consumed ?? prior?.Consumed, Pending = state.Pending ?? prior?.Pending };
            States.Add(preserved); StateWorkspaces.Add(workspace);
            return Task.FromResult(workspace);
        }
        public Task<ReviewFeedbackRecoveryState?> GetReviewFeedbackStateAsync(Workspace workspace, CancellationToken token) => Task.FromResult<ReviewFeedbackRecoveryState?>(States.LastOrDefault());
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState state, CancellationToken token) => throw new NotSupportedException(); public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => throw new NotSupportedException(); public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => throw new NotSupportedException();
    }
}
