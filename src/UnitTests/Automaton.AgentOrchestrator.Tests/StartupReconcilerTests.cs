using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.ReviewFeedback;
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
    public async Task RestartWithPendingCiVerifiesPublicationAndResumesExactHeadObservation()
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Pending, sha, 0)
        };
        var ci = new FakeCiShepherd { Result = CiShepherdResult.Success(sha, 0) };
        var fixture = Create(record, ciShepherd: ci);
        fixture.Publisher.Result = new(true, new("acme/widgets", "automaton/work/work-key", sha, 42, "https://github.com/acme/widgets/pull/42", true));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        Assert.Multiple(() => { Assert.That(ci.Targets.Single().HeadSha, Is.EqualTo(sha)); Assert.That(fixture.Executor.Attempts, Is.Empty); Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Completed)); });
    }

    [Test]
    public async Task RestartAfterCiPassResumesExactHeadHumanFeedbackRound()
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 3, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 1),
            ReviewFeedback = new(ReviewFeedbackStatuses.AwaitingHuman, sha, 2)
        };
        var ci = new FakeCiShepherd { Result = CiShepherdResult.Success(sha, 0) };
        var feedback = new FakeReviewFeedbackShepherd { Result = ReviewFeedbackResult.Success(sha, 2) };
        var fixture = Create(record, ciShepherd: ci, feedbackShepherd: feedback);
        fixture.Publisher.Result = new(true, new("acme/widgets", "automaton/work/work-key", sha, 42, "https://github.com/acme/widgets/pull/42", true));
        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await EventuallyAsync(() => feedback.Targets.Count == 1);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.FeedbackProvider.Targets.Single().HeadSha, Is.EqualTo(sha));
            Assert.That(feedback.Targets.Single().HeadSha, Is.EqualTo(sha));
            Assert.That(feedback.Targets.Single().RemediationRound, Is.EqualTo(2));
            Assert.That(feedback.Targets.Single().PublicationRevision, Is.EqualTo(3));
            Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
            Assert.That(ci.Targets, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Completed));
        });
    }

    [Test]
    [TestCase(ReviewFeedbackStatuses.AwaitingHuman)]
    [TestCase(ReviewFeedbackStatuses.Actionable)]
    public async Task PublishedPassedDraftFeedbackStateRestoresBoundedFeedbackMonitoringWithoutRerunningEngineering(string feedbackStatus)
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(feedbackStatus, sha, 0)
        };
        var feedback = new FakeReviewFeedbackShepherd { BlockUntilCancelled = true };
        var fixture = Create(record, feedbackShepherd: feedback);
        fixture.FeedbackProvider.Result = new ReviewFeedbackSnapshot(sha, feedbackStatus, feedbackStatus == ReviewFeedbackStatuses.Actionable
            ? [new ReviewFeedbackItem("comment-1", ReviewFeedbackKinds.ReviewComment, "human", "fix")]
            : []);
        feedback.StartupReconciled = () => fixture.Orchestrator.IsStartupReconciled;

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await EventuallyAsync(() => feedback.Targets.Count == 1);
        await fixture.Orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.True);
            Assert.That(feedback.WasStartupReconciledWhenStarted, Is.True);
            Assert.That(fixture.FeedbackProvider.Targets, Has.Count.EqualTo(1));
            Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
            Assert.That(fixture.Executor.Attempts, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Working && x.WorkItemId == "issue-1"));
        });

        await fixture.Orchestrator.StopAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoredActionableFeedbackPreservesDeveloperFailureSubcategoryAndRetryability(bool retryable)
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(ReviewFeedbackStatuses.Actionable, sha, 0)
        };
        var feedback = new FakeReviewFeedbackShepherd { Result = ReviewFeedbackResult.Blocked("human feedback Developer remediation failed", sha, 0, ReviewFeedbackFailureCategories.Developer, retryable) with { FailureSubcategory = "malformed-developer-contract" } };
        var fixture = Create(record, feedbackShepherd: feedback);
        fixture.FeedbackProvider.Result = new ReviewFeedbackSnapshot(sha, ReviewFeedbackStatuses.Actionable, [new("comment", ReviewFeedbackKinds.ReviewComment, "human", "fix")]);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);
        await EventuallyAsync(() => fixture.Telemetry.Events.Any(x => x.EventType == "human-feedback.monitor.failed"));

        var failed = fixture.Telemetry.Events.Single(x => x.EventType == "human-feedback.monitor.failed");
        Assert.Multiple(() =>
        {
            Assert.That(failed.Metadata!["category"], Is.EqualTo(ReviewFeedbackFailureCategories.Developer));
            Assert.That(failed.Metadata["subcategory"], Is.EqualTo("malformed-developer-contract"));
            Assert.That(failed.Metadata["retryable"], Is.EqualTo(retryable.ToString().ToLowerInvariant()));
            Assert.That(failed.Metadata["role"], Is.EqualTo(AgentRoles.Developer));
        });
        await fixture.Orchestrator.StopAsync();
    }

    [TestCase("ci")]
    [TestCase("feedback")]
    [TestCase("pull-request")]
    public async Task InconsistentPublishedAwaitingHumanStateFailsClosed(string mismatch)
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(ReviewFeedbackStatuses.AwaitingHuman, sha, 0)
        };
        record = mismatch switch
        {
            "ci" => record with { Ci = record.Ci! with { HeadSha = new string('b', 40) } },
            "feedback" => record with { ReviewFeedback = record.ReviewFeedback! with { HeadSha = new string('b', 40) } },
            "pull-request" => record with { Publication = record.Publication! with { PullRequestNumber = null } },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        var fixture = Create(record);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.True);
            Assert.That(fixture.FeedbackProvider.Targets, Is.Empty);
            Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked));
        });
    }

    [Test]
    public async Task GracefulShutdownThenRestartRestoresAwaitingHumanFeedbackAgain()
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(ReviewFeedbackStatuses.AwaitingHuman, sha, 0)
        };
        var firstFeedback = new FakeReviewFeedbackShepherd { BlockUntilCancelled = true };
        var first = Create(record, feedbackShepherd: firstFeedback);
        await first.Reconciler.ReconcileAsync(CancellationToken.None);
        await EventuallyAsync(() => firstFeedback.Targets.Count == 1);
        await first.Orchestrator.StopAsync();

        var secondFeedback = new FakeReviewFeedbackShepherd { BlockUntilCancelled = true };
        var second = Create(record, feedbackShepherd: secondFeedback);
        await second.Reconciler.ReconcileAsync(CancellationToken.None);
        await EventuallyAsync(() => secondFeedback.Targets.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(second.Orchestrator.IsStartupReconciled, Is.True);
            Assert.That(second.Executor.Attempts, Is.Empty);
            Assert.That(second.Publisher.RecoveryRequests, Is.Empty);
            Assert.That(second.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Working));
        });

        await second.Orchestrator.StopAsync();
    }

    [Test]
    public async Task QuarantinedPublishedAwaitingHumanWorkspaceNeverRestoresFeedbackMonitoring()
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Workspace = Record(WorkspaceRecoveryStates.Working).Workspace with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Quarantined },
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(ReviewFeedbackStatuses.AwaitingHuman, sha, 0)
        };
        var fixture = Create(record);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.FeedbackProvider.Targets, Is.Empty);
            Assert.That(fixture.Publisher.RecoveryRequests, Is.Empty);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Blocked));
        });
    }

    [Test]
    public void TransientAwaitingHumanProviderValidationKeepsStartupGateClosed()
    {
        var sha = new string('a', 40);
        var record = Record(WorkspaceRecoveryStates.Working) with
        {
            Publication = new(PublicationStates.PullRequestCreated, sha, "automaton/work/work-key", 42, "https://github.com/acme/widgets/pull/42", 1, new string('0', 40)),
            Ci = new(CiStatuses.Passed, sha, 0),
            ReviewFeedback = new(ReviewFeedbackStatuses.AwaitingHuman, sha, 0)
        };
        var provider = new FakeReviewFeedbackProvider { Failure = new ReviewFeedbackProviderException("provider unavailable", true) };
        var fixture = Create(record, feedbackShepherd: new FakeReviewFeedbackShepherd(), feedbackProvider: provider);

        Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => fixture.Reconciler.ReconcileAsync(CancellationToken.None));
        Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.False);
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

    private static Fixture Create(OwnedWorkspaceRecord record, WorkItem? item = null, string fenceClassification = ExecutionIdentityClassifications.Dead, FakeCiShepherd? ciShepherd = null, FakeReviewFeedbackShepherd? feedbackShepherd = null, FakeReviewFeedbackProvider? feedbackProvider = null)
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
        var telemetry = new FakeTelemetry();
        var orchestrator = new InMemoryOrchestrator(tracker, executor, options, new FixedClock(), NullLogger<InMemoryOrchestrator>.Instance, telemetry);
        var workspaces = new FakeWorkspaceManager(record);
        var fence = new FakeFence(fenceClassification);
        var publisher = new FakePublisher();
        feedbackProvider ??= new FakeReviewFeedbackProvider();
        var reconciler = new StartupReconciler(workspaces, tracker, fence, publisher, orchestrator, options, NullLogger<StartupReconciler>.Instance, ciShepherd, feedbackShepherd, feedbackProvider);
        return new(orchestrator, reconciler, tracker, executor, workspaces, fence, publisher, feedbackProvider, telemetry);
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
        RecordingExecutor Executor, FakeWorkspaceManager Workspaces, FakeFence Fence, FakePublisher Publisher, FakeReviewFeedbackProvider FeedbackProvider, FakeTelemetry Telemetry);
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-01-02T00:00:00Z"); }
    private sealed class FakeTelemetry : IOperatorTelemetry
    {
        public List<(string EventType, IReadOnlyDictionary<string, string>? Metadata)> Events { get; } = [];
        public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null) => Events.Add((eventType, metadata));
        public OperatorEvent? LatestFor(string workItemId) => null;
        public void SetDaemonState(string state, string? error = null) { }
        public (string State, string? LastError) DaemonState() => ("running", null);
    }
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
    private sealed class FakeCiShepherd : ICiShepherd
    {
        public List<CiTarget> Targets { get; } = [];
        public CiShepherdResult Result { get; set; } = CiShepherdResult.Blocked("not configured", new string('0', 40), 0);
        public Task<CiShepherdResult> ShepherdAsync(CiTarget target, Workspace workspace, WorkItem item, CancellationToken token) { Targets.Add(target); return Task.FromResult(Result); }
    }
    private sealed class FakeReviewFeedbackShepherd : IReviewFeedbackShepherd
    {
        public List<ReviewFeedbackTarget> Targets { get; } = [];
        public ReviewFeedbackResult Result { get; set; } = ReviewFeedbackResult.Blocked("not configured", new string('0', 40), 0);
        public bool BlockUntilCancelled { get; set; }
        public Func<bool>? StartupReconciled { get; set; }
        public bool? WasStartupReconciledWhenStarted { get; private set; }
        public async Task<ReviewFeedbackResult> ShepherdAsync(ReviewFeedbackTarget target, Workspace workspace, WorkItem item, CancellationToken token)
        {
            Targets.Add(target);
            WasStartupReconciledWhenStarted = StartupReconciled?.Invoke();
            if (BlockUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Result;
        }
    }
    private sealed class FakeReviewFeedbackProvider : IReviewFeedbackProvider
    {
        public List<ReviewFeedbackTarget> Targets { get; } = [];
        public ReviewFeedbackSnapshot? Result { get; set; }
        public ReviewFeedbackProviderException? Failure { get; set; }
        public Task<ReviewFeedbackSnapshot> GetAsync(ReviewFeedbackTarget target, CancellationToken cancellationToken)
        {
            Targets.Add(target);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Result ?? new ReviewFeedbackSnapshot(target.HeadSha, ReviewFeedbackStatuses.AwaitingHuman, []));
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
