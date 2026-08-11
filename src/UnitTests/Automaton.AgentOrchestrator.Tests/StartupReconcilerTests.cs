using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class StartupReconcilerTests
{
    [Test]
    public async Task PollingIsGatedUntilReconciliationCompletes()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Ready));
        await fixture.Orchestrator.PollAsync(CancellationToken.None);
        Assert.That(fixture.Executor.Attempts, Is.Empty);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => fixture.Executor.Attempts.Count == 1);
    }

    [TestCase(WorkspaceRecoveryStates.Completed, RunStates.Completed)]
    [TestCase(WorkspaceRecoveryStates.Blocked, RunStates.Blocked)]
    [TestCase(WorkspaceRecoveryStates.Cancelled, RunStates.Cancelled)]
    public async Task DurableTerminalOutcomeSuppressesRedispatch(string recoveryState, string runState)
    {
        var fixture = Create(Record(recoveryState, lastAttempt: 2, nextAttempt: 3));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Executor.Attempts, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == runState && x.Attempt == 2));
        });
    }

    [Test]
    public async Task DurableRetryRestoresIncrementedAttempt()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.RetryableFailure, lastAttempt: 2, nextAttempt: 3));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => fixture.Executor.Attempts.Count == 1);

        Assert.That(fixture.Executor.Attempts.Single().Attempt, Is.EqualTo(3));
    }

    [Test]
    public async Task DurableVerifiedDraftPublicationSuppressesRedispatchAfterCrashWindow()
    {
        var publication = new PublicationRecoveryState(PublicationStates.PullRequestCreated, new string('a', 40), "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42");
        var fixture = Create(Record(WorkspaceRecoveryStates.Working) with { Publication = publication });
        fixture.Publisher.Result = new(true, new("acme/widgets", "automaton/work/work-key", new string('a', 40), 42, "https://github.com/acme/widgets/pull/42", true));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Executor.Attempts, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Completed));
        });
    }

    [TestCase(PublicationStates.Authorized)]
    [TestCase(PublicationStates.Committed)]
    [TestCase(PublicationStates.Pushed)]
    public async Task PartialPublicationRecoveryCompletesWithoutRunningRoleExecutor(string publicationState)
    {
        var publication = new PublicationRecoveryState(publicationState, new string('a', 40), "automaton/work/work-key");
        var fixture = Create(Record(WorkspaceRecoveryStates.Working) with { Publication = publication });
        fixture.Publisher.Result = new(true, new("acme/widgets", "automaton/work/work-key", new string('a', 40), 42, "https://github.com/acme/widgets/pull/42", true));

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() => { Assert.That(fixture.Publisher.RecoveryRequests, Has.Count.EqualTo(1)); Assert.That(fixture.Executor.Attempts, Is.Empty); Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Completed)); });
    }

    [Test]
    public async Task WorkingWithoutDurableAuthorizationNeverEntersPublicationRecovery()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working));
        fixture.Publisher.Result = new(true, new("acme/widgets", "automaton/work/work-key", new string('a', 40), 42, "https://github.com/acme/widgets/pull/42", true));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);
        Assert.Multiple(() => { Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty); Assert.That(fixture.Executor.Attempts, Is.Empty); Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked)); });
    }

    [Test]
    public void TransientPublicationVerificationFailureKeepsStartupGateClosed()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working) with { Publication = new(PublicationStates.PullRequestCreated, new string('a', 40), "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42") });
        fixture.Publisher.Failure = new PublicationException("provider unavailable", true);
        Assert.ThrowsAsync<PublicationException>(() => fixture.Reconciler.ReconcileAsync(CancellationToken.None));
        Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.False);
    }

    [Test]
    public async Task MissingOrMismatchedPullRequestBecomesBlockedNotCompleted()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working) with { Publication = new(PublicationStates.PullRequestCreated, new string('a', 40), "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42") });
        fixture.Publisher.Failure = new PublicationException("draft pull request no longer matches");
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Multiple(() => { Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked)); Assert.That(fixture.Orchestrator.Snapshot(), Has.None.Matches<RunState>(x => x.State == RunStates.Completed)); });
    }

    [TestCase("closed", true)]
    [TestCase("open", false)]
    [TestCase("open", true, "bug")]
    public async Task IneligibleTrackerStatePreventsPublicationRecovery(string providerState, bool present, string? label = "agent:ready")
    {
        var record = Record(WorkspaceRecoveryStates.Working) with { Publication = new(PublicationStates.Pushed, new string('a', 40), "automaton/work/work-key") };
        var item = present ? Work(labels: label is null ? [] : [label], providerState: providerState) : null;
        var fixture = Create(record, item);
        if (!present) fixture.Tracker.ReturnNull = true;
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
    }

    [Test]
    public async Task RetryWhoseTrackerItemBecameIneligibleDoesNotRun()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.RetryableFailure), Work(labels: ["bug"]));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Executor.Attempts, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Ineligible));
        });
    }

    [Test]
    public async Task RetryWhoseTrackerItemClosedDoesNotRun()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.RetryableFailure), Work(providerState: "closed"));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);
        Assert.That(fixture.Executor.Attempts, Is.Empty);
    }

    [TestCase(ExecutionIdentityClassifications.Ambiguous)]
    [TestCase(ExecutionIdentityClassifications.Reused)]
    public async Task AmbiguousOrReusedPidIsNeverTerminatedAndIsQuarantined(string classification)
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working, active: true), fenceClassification: classification);
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Fence.TerminationCount, Is.Zero);
            Assert.That(fixture.Executor.Attempts, Is.Empty);
            Assert.That(fixture.Workspaces.Records.Single().Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked));
            Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
        });
    }

    [TestCase(ExecutionFencingKinds.WindowsJob)]
    [TestCase(ExecutionFencingKinds.UnixProcessGroup)]
    public async Task MatchingLiveOwnedProcessIsTerminatedBeforeOwnershipIsReleased(string fencingKind)
    {
        var record = Record(WorkspaceRecoveryStates.Working, active: true, fencingKind: fencingKind);
        var fixture = Create(record, fenceClassification: ExecutionIdentityClassifications.MatchingLive);
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Fence.TerminationCount, Is.EqualTo(1));
            Assert.That(fixture.Workspaces.Records.Single().Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
        });
    }

    [Test]
    public async Task DeadOwnedProcessReleasesOwnershipWithoutTermination()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working, active: true), fenceClassification: ExecutionIdentityClassifications.Dead);
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Fence.TerminationCount, Is.Zero);
            Assert.That(fixture.Workspaces.Records.Single().Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
        });
    }

    [Test]
    public async Task FailedVerifiedTerminationQuarantinesWorkspace()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Working, active: true), fenceClassification: ExecutionIdentityClassifications.MatchingLive);
        fixture.Fence.TerminationSucceeds = false;
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.That(fixture.Workspaces.Records.Single().Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
    }

    [Test]
    public async Task ExistingQuarantineRemainsBlockedWithoutInspection()
    {
        var record = Record(WorkspaceRecoveryStates.Ready) with
        {
            Workspace = Record(WorkspaceRecoveryStates.Ready).Workspace with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Quarantined }
        };
        var fixture = Create(record);
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Fence.InspectionCount, Is.Zero);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked));
        });
    }

    [Test]
    public void TrackerFailureKeepsPollingDisabled()
    {
        var fixture = Create(Record(WorkspaceRecoveryStates.Ready));
        fixture.Tracker.Failure = new InvalidOperationException("provider unavailable");
        Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Reconciler.ReconcileAsync(CancellationToken.None));
        Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.False);
    }

    private static Fixture Create(OwnedWorkspaceRecord record, WorkItem? item = null, string fenceClassification = ExecutionIdentityClassifications.Dead)
    {
        var tracker = new FakeTracker(item ?? Work());
        var executor = new RecordingExecutor();
        var options = new OrchestratorOptions
        {
            Repository = new RepositoryOptions { Root = "C:/canonical", Identity = "acme/widgets" },
            Tracker = new TrackerOptions { RequiredLabels = ["agent:ready"], ActiveProviderLifecycleStates = ["open"] },
            Agent = new AgentExecutionOptions { MaxConcurrentAgents = 1 },
            Retry = new RetryOptions { MaxAttempts = 3 }
        };
        var orchestrator = new InMemoryOrchestrator(tracker, executor, options, new FixedClock(), NullLogger<InMemoryOrchestrator>.Instance);
        var workspaces = new FakeWorkspaceManager(record);
        var fence = new FakeFence(fenceClassification);
        var publisher = new FakePublisher();
        var reconciler = new StartupReconciler(workspaces, tracker, fence, publisher, orchestrator, options, NullLogger<StartupReconciler>.Instance);
        return new(orchestrator, reconciler, tracker, executor, workspaces, fence, publisher);
    }

    private static WorkItem Work(IReadOnlyList<string>? labels = null, string providerState = "open") => new("issue-1", "#1", "Work", null, providerState, labels ?? ["agent:ready"]);

    private static OwnedWorkspaceRecord Record(string recovery, int lastAttempt = 0, int nextAttempt = 1, bool active = false,
        string fencingKind = ExecutionFencingKinds.PortableProcessTree)
    {
        const string nonce = "launch-1";
        var workspace = new Workspace("work-key", "C:/workspaces/work-key", "automaton/work/work-key", WorkspaceLifecycleStates.Ready,
            active ? WorkspaceExecutionOwnershipStates.Active : WorkspaceExecutionOwnershipStates.None, nonce, "acme/widgets");
        var identity = active ? new DurableExecutionIdentity(123, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), nonce, fencingKind, 123) : null;
        return new(workspace, "issue-1", new WorkspaceRepository("C:/canonical", "acme/widgets"),
            new WorkspaceRecoveryState(recovery, lastAttempt, nextAttempt), identity, 2);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var index = 0; index < 100; index++) { if (condition()) return; await Task.Delay(10); }
        Assert.Fail("Timed out waiting for execution.");
    }

    private sealed record Fixture(InMemoryOrchestrator Orchestrator, StartupReconciler Reconciler, FakeTracker Tracker,
        RecordingExecutor Executor, FakeWorkspaceManager Workspaces, FakeFence Fence, FakePublisher Publisher);
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-02T00:00:00Z"); }
    private sealed class FakeTracker(WorkItem item) : IWorkTracker
    {
        public Exception? Failure { get; set; }
        public bool ReturnNull { get; set; }
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkItem>>([item]);
        public Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken)
            => Failure is not null ? Task.FromException<WorkItem?>(Failure) : Task.FromResult<WorkItem?>(ReturnNull ? null : item);
    }
    private sealed class RecordingExecutor : IWorkExecutor
    {
        public List<RunAttempt> Attempts { get; } = [];
        public Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken) { Attempts.Add(attempt); return Task.FromResult(RunOutcome.Success()); }
    }
    private sealed class FakePublisher : IWorkPublisher
    {
        public List<OwnedWorkspaceRecord> RecoveryRequests { get; } = [];
        public PublicationRecoveryResult Result { get; set; } = new(false);
        public PublicationException? Failure { get; set; }
        public Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PublicationRecoveryResult> RecoverAsync(OwnedWorkspaceRecord record, WorkItem workItem, CancellationToken cancellationToken)
        {
            RecoveryRequests.Add(record);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Result);
        }
    }
    private sealed class FakeFence(string classification) : IExecutionFence
    {
        public int TerminationCount { get; private set; }
        public int InspectionCount { get; private set; }
        public bool TerminationSucceeds { get; set; } = true;
        public Task<FencedProcess> LaunchAsync(System.Diagnostics.ProcessStartInfo startInfo, string launchNonce, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExecutionIdentityInspection> InspectAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken) { InspectionCount++; return Task.FromResult(new ExecutionIdentityInspection(classification)); }
        public Task<bool> TerminateAndVerifyAsync(DurableExecutionIdentity identity, TimeSpan timeout, CancellationToken cancellationToken) { TerminationCount++; return Task.FromResult(TerminationSucceeds); }
    }
    private sealed class FakeWorkspaceManager(OwnedWorkspaceRecord record) : IWorkspaceManager
    {
        public List<OwnedWorkspaceRecord> Records { get; } = [record];
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([.. Records]);
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string status, string nonce, CancellationToken cancellationToken)
        {
            var updated = workspace with { ExecutionOwnershipStatus = status, LaunchNonce = nonce };
            Records[0] = Records[0] with { Workspace = updated, ExecutionIdentity = status == WorkspaceExecutionOwnershipStates.None ? null : Records[0].ExecutionIdentity };
            return Task.FromResult(updated);
        }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string lifecycleStatus, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState publicationState, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
