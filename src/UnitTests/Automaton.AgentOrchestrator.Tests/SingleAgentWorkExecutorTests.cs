using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class SingleAgentWorkExecutorTests
{
    private const string CanonicalRoot = "C:\\canonical-repository";
    private const string WorkspacePath = "C:\\isolated-workspaces\\work-123";

    [Test]
    public async Task SuccessfulCiPersistsAuthoritativeWorkspaceThroughFeedbackAndLifecycle()
    {
        var c = Workspace("nonce-c");
        var ci = new FakeCi(CiShepherdResult.Success(new string('a', 40), 0, c));
        var feedback = new FakeFeedback();
        var fixture = CreateCore(3, [], [ManagerReady(), DeveloperCompleted(), ReviewerApproved()], ci, feedback);
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(outcome.Succeeded, Is.True); Assert.That(feedback.Workspaces.Single().LaunchNonce, Is.EqualTo("nonce-c")); Assert.That(fixture.Workspaces.RecoveryInputs.Last().LaunchNonce, Is.EqualTo("nonce-c")); Assert.That(fixture.Workspaces.LifecycleInputs.Last().LaunchNonce, Is.EqualTo("nonce-c")); });
    }

    [Test]
    public async Task BlockedCiPersistsAuthoritativeWorkspaceThroughRecoveryAndLifecycle()
    {
        var c = Workspace("nonce-c");
        var ci = new FakeCi(CiShepherdResult.Blocked("failed", new string('a', 40), 0, c));
        var fixture = CreateCore(3, [], [ManagerReady(), DeveloperCompleted(), ReviewerApproved()], ci, null);
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(outcome.Succeeded, Is.False); Assert.That(fixture.Workspaces.RecoveryInputs.Last().LaunchNonce, Is.EqualTo("nonce-c")); Assert.That(fixture.Workspaces.LifecycleInputs.Last().LaunchNonce, Is.EqualTo("nonce-c")); });
    }

    [Test]
    public async Task PublicationWorkspaceIsAuthoritativeInputToCiAfterEngineeringClientRelease()
    {
        var published = Workspace("nonce-published");
        var ci = new FakeCi(CiShepherdResult.Blocked("failed", new string('a', 40), 0, published));
        var fixture = CreateCore(3, [], [ManagerReady(), DeveloperCompleted(), ReviewerApproved()], ci, null);
        fixture.Publisher.PublishedWorkspace = published;

        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.That(ci.Workspaces.Single().LaunchNonce, Is.EqualTo("nonce-published"));
    }

    [Test]
    public async Task CodexCiRemediatorReleasesExactlyOnceBeforeReturning()
    {
        var workspace = Workspace("nonce-b");
        var client = new FakeCodexClient(workspace, [DeveloperCompleted(), ReviewerApproved()]);
        var factory = new FakeCodexFactory(client);
        var publisher = new FakePublisher();
        var workflow = new WorkflowDefinition(Path.Combine(CanonicalRoot, "WORKFLOW.md"), new Dictionary<string, object?>(), "guidance", new OrchestratorOptions { Agent = new AgentExecutionOptions { DeveloperMaxTurns = 2, ReviewerMaxTurns = 2, MaxReviewRounds = 1 } });
        var remediator = new Automaton.AgentOrchestrator.Ci.CodexCiRemediator(factory, new FakeMutationGuard([]), publisher, workflow);
        var request = new CiRemediationRequest(new("acme/widgets", 1, "automaton/work/work-123", new string('a', 40)), workspace, Attempt().WorkItem, new CiSnapshot(new string('a', 40), CiStatuses.Failed, []), 1);
        var result = await remediator.RemediateAsync(request, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(client.DisposeCount, Is.EqualTo(1)); Assert.That(result.Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None)); Assert.That(result.Workspace.LaunchNonce, Is.EqualTo("nonce-published")); });
    }

    [Test]
    public async Task CodexCiRemediatorDisposesOnceWhenDeveloperFails()
    {
        var client = new FakeCodexClient(Workspace("nonce-b"), [DeveloperCompleted()]) { WaitFailure = new InvalidOperationException("turn failed") };
        var workflow = new WorkflowDefinition(Path.Combine(CanonicalRoot, "WORKFLOW.md"), new Dictionary<string, object?>(), "guidance", new OrchestratorOptions { Agent = new AgentExecutionOptions { DeveloperMaxTurns = 1, ReviewerMaxTurns = 1 } });
        var remediator = new Automaton.AgentOrchestrator.Ci.CodexCiRemediator(new FakeCodexFactory(client), new FakeMutationGuard([]), new FakePublisher(), workflow);
        var request = new CiRemediationRequest(new("acme/widgets", 1, "automaton/work/work-123", new string('a', 40)), Workspace("nonce-b"), Attempt().WorkItem, new CiSnapshot(new string('a', 40), CiStatuses.Failed, []), 1);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await remediator.RemediateAsync(request, CancellationToken.None));
        Assert.That(client.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LaunchFailureIsObservableWhileQuarantineRemainsFailClosed()
    {
        var fixture = Create();
        fixture.Client.Quarantined = true;
        fixture.Client.InitializeFailure = new CodexLaunchException("process-start", "The Codex process could not be started.");

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        var launchEvent = fixture.Telemetry.Events.Single(value => value.EventType == "codex.launch.failed");
        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("Codex launch failed; execution ownership was quarantined."));
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Telemetry.Events.Count(value => value.EventType == "codex.launch.failed"), Is.EqualTo(1));
            Assert.That(launchEvent.Message, Does.Not.Contain("process-start"));
            Assert.That(launchEvent.Metadata!["category"], Is.EqualTo("process-start"));
            Assert.That(launchEvent.Metadata["reason"], Is.EqualTo("process start failed"));
        });
    }

    [Test]
    public async Task IdleTimeoutIsObservableOnceWithoutChangingRetryClassification()
    {
        var fixture = Create(ManagerReady());
        fixture.Client.WaitFailure = new CodexIdleTimeoutException();
        fixture.Client.LastEventTypeValue = "item/completed";
        fixture.Client.LastActivityValue = DateTimeOffset.UtcNow.AddMinutes(-15);
        fixture.Client.IdleTimeoutValue = TimeSpan.FromMinutes(15);
        fixture.Client.ProcessAliveValue = true;

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        var events = fixture.Telemetry.Events.Where(value => value.EventType == "codex.turn.idle_timeout").ToList();
        var diagnostic = events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(diagnostic.Metadata!["lastEventType"], Is.EqualTo("item/completed"));
            Assert.That(diagnostic.Metadata["processAlive"], Is.EqualTo("true"));
            Assert.That(diagnostic.Message, Does.Not.Contain("item/completed"));
            Assert.That(diagnostic.Metadata.Values, Has.None.Contain("secret"));
        });
    }

    [Test]
    public async Task RolesRunInOrderOnDistinctThreadsUnderOneClientLease()
    {
        var fixture = Create(ManagerReady(), DeveloperCompleted(), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Client.StartedThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3" }));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3" }));
            Assert.That(fixture.Client.Prompts[0], Does.Contain("ROLE: MANAGER"));
            Assert.That(fixture.Client.Prompts[1], Does.Contain("ROLE: DEVELOPER"));
            Assert.That(fixture.Client.Prompts[2], Does.Contain("ROLE: REVIEWER"));
            Assert.That(fixture.Factory.CreateCount, Is.EqualTo(1));
            Assert.That(fixture.Client.Disposed, Is.True);
            Assert.That(fixture.Client.DisposeCount, Is.EqualTo(1));
            Assert.That(fixture.Publisher.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.Publisher.Requests[0].Reviewer.Verdict, Is.EqualTo(RoleResultStatuses.Approved));
        });
    }

    [Test]
    public async Task ManagerBlockedStopsBeforeDeveloperOrReviewer()
    {
        var fixture = Create(ManagerBlocked("missing decision"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("missing decision"));
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(1));
            Assert.That(fixture.Publisher.Requests, Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PublicationFailureIsClassifiedWithoutRepeatingRoleExecution(bool retryable)
    {
        var fixture = Create(ManagerReady(), DeveloperCompleted(), ReviewerApproved());
        fixture.Publisher.Failure = new PublicationException("publication failed", retryable);

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.EqualTo(retryable));
            Assert.That(outcome.FailureReason, Is.EqualTo("publication failed"));
            Assert.That(fixture.Publisher.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task DeveloperBlockedStopsBeforeReviewer()
    {
        var fixture = Create(ManagerReady(), DeveloperBlocked("cannot compile"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(2));
            Assert.That(fixture.Client.Prompts, Has.None.Contains("ROLE: REVIEWER"));
        });
    }

    [Test]
    public async Task ReviewerBlockedIsPermanentWithoutRemediation()
    {
        var fixture = Create(ManagerReady(), DeveloperCompleted(), Reviewer("BLOCKED", "review did not approve"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("review did not approve"));
        });
    }

    [Test]
    public async Task ChangesRequiredRunsRemediationAndReReviewOnExistingThreadsUntilApproved()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(),
            Reviewer("CHANGES_REQUIRED", "fix validation", "add regression test"),
            DeveloperCompleted(), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Client.StartedThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3" }));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3", "thread-2", "thread-3" }));
            Assert.That(fixture.Client.Prompts[3], Does.Contain("REMEDIATION FOR REVIEW ROUND 2"));
            Assert.That(fixture.Client.Prompts[3], Does.Contain("add regression test"));
            Assert.That(fixture.Client.Prompts[4], Does.Contain("REVIEW ROUND 2"));
        });
    }

    [Test]
    public async Task TwoRemediationsCanReachApprovalOnThirdReview()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "round one", "finding A"),
            DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "round two", "finding C"),
            DeveloperCompleted(), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(3));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3", "thread-2", "thread-3", "thread-2", "thread-3" }));
            Assert.That(fixture.Client.Prompts[5], Does.Contain("finding C"));
            Assert.That(fixture.Client.Prompts[5], Does.Not.Contain("finding A"));
        });
    }

    [Test]
    public async Task ReviewRoundLimitEndsInDeterministicManualAttention()
    {
        var fixture = CreateWithReviewRounds(2,
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "one", "A"),
            DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "two", "B"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("Maximum review rounds reached without approval."));
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task DeveloperBlockedDuringRemediationStopsBeforeAnotherReview()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "fix needed", "A"),
            DeveloperBlocked("remediation unavailable"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("remediation unavailable"));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3", "thread-2" }));
        });
    }

    [Test]
    public async Task RemediationPromptsUseLatestStructuredHandoffsWithoutRawTranscriptsOrSecrets()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(finalPrefix: "RAW_DEVELOPER_TRANSCRIPT\n"),
            Reviewer("CHANGES_REQUIRED", "fix now", "blocking issue", "RAW_REVIEWER_TRANSCRIPT\n"),
            DeveloperCompleted(), ReviewerApproved());
        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        var remediation = fixture.Client.Prompts[3];
        var reReview = fixture.Client.Prompts[4];

        Assert.Multiple(() =>
        {
            Assert.That(remediation, Does.Contain("fix now"));
            Assert.That(remediation, Does.Contain("blocking issue"));
            Assert.That(remediation, Does.Contain("Implement widget safely"));
            Assert.That(remediation, Does.Contain("Implementation complete"));
            Assert.That(remediation, Does.Not.Contain("RAW_REVIEWER_TRANSCRIPT"));
            Assert.That(remediation, Does.Not.Contain("RAW_DEVELOPER_TRANSCRIPT"));
            Assert.That(remediation, Does.Not.Contain(CanonicalRoot));
            Assert.That(reReview, Does.Contain("inspect the actual current workspace"));
            Assert.That(reReview, Does.Contain("do not trust Developer claims blindly"));
            Assert.That(reReview, Does.Contain("regressions or new blocking issues"));
        });
    }

    [TestCase(0, "Manager")]
    [TestCase(1, "Developer")]
    [TestCase(2, "Reviewer")]
    public async Task MalformedRoleContractFailsClosed(int malformedIndex, string role)
    {
        var results = new[] { ManagerReady(), DeveloperCompleted(), ReviewerApproved() };
        results[malformedIndex] = Completed("unstructured transcript only");
        var fixture = Create(results);
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.That(outcome.FailureReason, Does.Contain($"{role} returned a malformed"));
    }

    [Test]
    public async Task ContinuationTurnsReuseOnlyTheirRoleThread()
    {
        var fixture = Create(
            Manager("CONTINUE", "inspect more"), ManagerReady(),
            Developer("CONTINUE", "tests remain"), DeveloperCompleted(),
            Reviewer("CONTINUE", "run checks"), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-1", "thread-2", "thread-2", "thread-3", "thread-3" }));
            Assert.That(fixture.Client.Prompts[1], Does.Contain("inspect more"));
            Assert.That(fixture.Client.Prompts[3], Does.Contain("tests remain"));
        });
    }

    [Test]
    public async Task StructuredHandoffsArePassedWithoutRawTranscripts()
    {
        var fixture = Create(
            ManagerReady(finalPrefix: "RAW_MANAGER_TRANSCRIPT_DO_NOT_PROPAGATE\n"),
            DeveloperCompleted(finalPrefix: "RAW_DEVELOPER_TRANSCRIPT_DO_NOT_PROPAGATE\n"),
            ReviewerApproved());
        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Client.Prompts[1], Does.Contain("Implement widget safely"));
            Assert.That(fixture.Client.Prompts[1], Does.Not.Contain("RAW_MANAGER_TRANSCRIPT"));
            Assert.That(fixture.Client.Prompts[2], Does.Contain("Implementation complete"));
            Assert.That(fixture.Client.Prompts[2], Does.Contain("Full issue body"));
            Assert.That(fixture.Client.Prompts[2], Does.Not.Contain("RAW_MANAGER_TRANSCRIPT"));
            Assert.That(fixture.Client.Prompts[2], Does.Not.Contain("RAW_DEVELOPER_TRANSCRIPT"));
        });
    }

    [Test]
    public async Task PromptsPreserveTrustWorkspaceAndSecretBoundaries()
    {
        const string token = "github-token-must-not-leak";
        var fixture = Create(ManagerReady(), DeveloperCompleted(), ReviewerApproved());
        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        var combined = string.Join("\n", fixture.Client.Prompts);

        Assert.Multiple(() =>
        {
            Assert.That(combined, Does.Contain("untrusted task content"));
            Assert.That(combined, Does.Contain("assigned_workspace"));
            Assert.That(combined, Does.Contain(WorkspacePath.Replace("\\", "\\\\", StringComparison.Ordinal)));
            Assert.That(combined, Does.Contain("root AGENTS.md"));
            Assert.That(combined, Does.Contain("Do not push"));
            Assert.That(combined, Does.Contain("create a PR"));
            Assert.That(combined, Does.Contain("mutate provider state"));
            Assert.That(combined, Does.Not.Contain(CanonicalRoot));
            Assert.That(combined, Does.Not.Contain(token));
            Assert.That(fixture.Factory.ChildEnvironment, Is.Empty);
            Assert.That(fixture.Factory.CreatedWorkspace!.Path, Is.EqualTo(WorkspacePath));
        });
    }

    [Test]
    public async Task ManagerWorkspaceModificationStopsBeforeDeveloper()
    {
        var fixture = CreateWithFingerprints(["before", "after"], ManagerReady());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.FailureReason, Does.Contain("manager violated its read-only"));
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ReviewerWorkspaceModificationFailsClosed()
    {
        var fixture = CreateWithFingerprints(["manager", "manager", "review-before", "review-after"], ManagerReady(), DeveloperCompleted(), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("reviewer violated its read-only"));
        });
    }

    [Test]
    public async Task FailedManagerWithUnchangedFingerprintIsRetryableAndPreservesWorkspace()
    {
        var fixture = Create(FailedTurn());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.RetryableFailure }));
            Assert.That(fixture.Client.Disposed, Is.True);
        });
    }

    [TestCase("failed")]
    [TestCase("interrupted")]
    public async Task ManagerMutationOverridesTransientTurnFailure(string turnStatus)
    {
        var fixture = CreateWithFingerprints(["before", "after"], TurnFailure(turnStatus));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("manager violated its read-only"));
            Assert.That(fixture.Client.StartedThreadIds, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ReviewerMutationOverridesTransientTurnFailure()
    {
        var fixture = CreateWithFingerprints(
            ["manager", "manager", "review-before", "review-after"],
            ManagerReady(), DeveloperCompleted(), FailedTurn());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("reviewer violated its read-only"));
        });
    }

    [Test]
    public async Task FailedReviewerWithUnchangedFingerprintRemainsRetryable()
    {
        var fixture = Create(ManagerReady(), DeveloperCompleted(), FailedTurn());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(outcome.FailureReason, Is.EqualTo("temporary model failure"));
        });
    }

    [Test]
    public async Task ReviewerMutationDuringReReviewBlocks()
    {
        var fixture = CreateWithFingerprints(
            ["manager", "manager", "review-1", "review-1", "review-2-before", "review-2-after"],
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "fix", "A"),
            DeveloperCompleted(), ReviewerApproved());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("reviewer violated its read-only"));
        });
    }

    [Test]
    public async Task FailedReReviewWithUnchangedFingerprintRemainsRetryable()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "fix", "A"),
            DeveloperCompleted(), FailedTurn());
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.That(outcome.IsRetryable, Is.True);
    }

    [Test]
    public void CancellationStillPersistsAndPropagates()
    {
        var fixture = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Executor.ExecuteAsync(Attempt(), cancellation.Token));
        Assert.That(fixture.Client.Disposed, Is.True);
    }

    [Test]
    public async Task ProtocolAmbiguityPreservesQuarantine()
    {
        var fixture = Create();
        fixture.Client.InitializeFailure = new CodexProtocolException("malformed response");
        fixture.Client.Quarantined = true;
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Publisher.Requests, Is.Empty);
            Assert.That(fixture.Workspaces.Lifecycles, Is.Empty);
        });
    }

    [Test]
    public async Task ProtocolAmbiguityDuringReadOnlyRoleBypassesMutationDowngradeAndPreservesQuarantine()
    {
        var fixture = CreateWithFingerprints(["before", "after"]);
        fixture.Client.WaitFailure = new CodexProtocolException("transport ambiguity");
        fixture.Client.Quarantined = true;

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Workspaces.Lifecycles, Is.Empty);
        });
    }

    [Test]
    public async Task ProtocolAmbiguityDuringReReviewPreservesQuarantine()
    {
        var fixture = Create(
            ManagerReady(), DeveloperCompleted(), Reviewer("CHANGES_REQUIRED", "fix", "A"),
            DeveloperCompleted());
        fixture.Client.WaitFailure = new CodexProtocolException("re-review transport ambiguity");
        fixture.Client.WaitFailureAtCall = 5;
        fixture.Client.Quarantined = true;

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Workspaces.Lifecycles, Is.Empty);
        });
    }

    [Test]
    public void ReviewerContractRejectsTooManyOrOversizedFindings()
    {
        var tooMany = string.Join(',', Enumerable.Range(1, 21).Select(_ => "{\"severity\":\"minor\",\"description\":\"x\"}"));
        Assert.Multiple(() =>
        {
            Assert.That(RoleResultContracts.TryParseReviewer($"AUTOMATON_REVIEW_RESULT: {{\"verdict\":\"APPROVED\",\"summary\":\"ok\",\"findings\":[{tooMany}]}}", out _), Is.False);
            Assert.That(RoleResultContracts.TryParseReviewer(Reviewer("APPROVED", "ok", new string('x', 513)).FinalResponse, out _), Is.False);
        });
    }

    private static Fixture Create(params CodexTurnResult[] results) => CreateCore(3, [], results);

    private static Fixture CreateWithReviewRounds(int maxReviewRounds, params CodexTurnResult[] results)
        => CreateCore(maxReviewRounds, [], results);

    private static Fixture CreateWithFingerprints(IEnumerable<string> fingerprints, params CodexTurnResult[] results)
        => CreateCore(3, fingerprints, results);

    private static Fixture CreateCore(int maxReviewRounds, IEnumerable<string> fingerprints, CodexTurnResult[] results, ICiShepherd? ci = null, IReviewFeedbackShepherd? feedback = null)
    {
        var workspace = new Workspace("work-123", WorkspacePath, "automaton/work/work-123", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "reserved", "acme/widgets");
        var manager = new FakeWorkspaceManager(workspace);
        var guard = new FakeMutationGuard(fingerprints);
        var client = new FakeCodexClient(workspace, results);
        var factory = new FakeCodexFactory(client);
        var workflow = new WorkflowDefinition(Path.Combine(CanonicalRoot, "WORKFLOW.md"), new Dictionary<string, object?>(), "Repository-owned workflow guidance.", new OrchestratorOptions
        {
            Repository = new RepositoryOptions { Root = CanonicalRoot, Identity = "acme/widgets" },
            Workspace = new WorkspaceOptions { Root = "C:\\isolated-workspaces" },
            Agent = new AgentExecutionOptions { MaxConcurrentAgents = 1, MaxTurns = 2, ManagerMaxTurns = 2, DeveloperMaxTurns = 2, ReviewerMaxTurns = 2, MaxReviewRounds = maxReviewRounds }
        });
        var publisher = new FakePublisher();
        var telemetry = new FakeTelemetry();
        return new(new RoleBasedWorkExecutor(manager, guard, factory, publisher, workflow, ci, feedback, telemetry), manager, factory, client, publisher, telemetry);
    }

    private static RunAttempt Attempt() => new(new WorkItem("issue-42", "ACME-42", "Correct the widget", "Full issue body", "open", ["agent:ready", "bug"]), 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private static Workspace Workspace(string nonce) => new("work-123", WorkspacePath, "automaton/work/work-123", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, nonce, "acme/widgets");
    private static CodexTurnResult ManagerReady(string finalPrefix = "") => Manager("READY", "plan ready", finalPrefix);
    private static CodexTurnResult ManagerBlocked(string reason) => Manager("BLOCKED", reason);
    private static CodexTurnResult Manager(string status, string summary, string finalPrefix = "") => Completed($"{finalPrefix}AUTOMATON_MANAGER_RESULT: {{\"status\":\"{status}\",\"summary\":\"{summary}\",\"implementationPlan\":\"Implement widget safely\",\"validationPlan\":\"Run tests\",\"risks\":\"None\"}}");
    private static CodexTurnResult DeveloperCompleted(string finalPrefix = "") => Developer("COMPLETED", "Implementation complete", finalPrefix);
    private static CodexTurnResult DeveloperBlocked(string reason) => Developer("BLOCKED", reason);
    private static CodexTurnResult Developer(string status, string summary, string finalPrefix = "") => Completed($"{finalPrefix}AUTOMATON_DEVELOPER_RESULT: {{\"status\":\"{status}\",\"summary\":\"{summary}\",\"validation\":\"Tests passed\",\"remainingConcerns\":\"None\"}}");
    private static CodexTurnResult ReviewerApproved() => Reviewer("APPROVED", "review approved");
    private static CodexTurnResult Reviewer(string verdict, string summary, string finding = "", string finalPrefix = "") => Completed($"{finalPrefix}AUTOMATON_REVIEW_RESULT: {{\"verdict\":\"{verdict}\",\"summary\":\"{summary}\",\"findings\":[{{\"severity\":\"minor\",\"description\":\"{finding}\"}}]}}");
    private static CodexTurnResult Completed(string response) => new("unused", Guid.NewGuid().ToString("N"), "completed", FinalResponse: response);
    private static CodexTurnResult FailedTurn() => TurnFailure("failed");
    private static CodexTurnResult TurnFailure(string status) => new("unused", Guid.NewGuid().ToString("N"), status, "temporary model failure");

    private sealed record Fixture(RoleBasedWorkExecutor Executor, FakeWorkspaceManager Workspaces, FakeCodexFactory Factory, FakeCodexClient Client, FakePublisher Publisher, FakeTelemetry Telemetry);
    private sealed class FakeTelemetry : IOperatorTelemetry
    {
        public List<OperatorEvent> Events { get; } = [];
        public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null)
            => Events.Add(new(Events.Count + 1, DateTimeOffset.UtcNow, eventType, severity, workItemId, stage, role, message, metadata));
        public OperatorEvent? LatestFor(string workItemId) => Events.LastOrDefault(value => value.WorkItemId == workItemId);
        public void SetDaemonState(string state, string? error = null) { }
        public (string State, string? LastError) DaemonState() => (OperatorDaemonStates.Running, null);
    }
    private sealed class FakePublisher : IWorkPublisher
    {
        public Task<PublicationResult> VerifyRecoveredPublicationAsync(OwnedWorkspaceRecord record, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public List<PublicationRequest> Requests { get; } = [];
        public PublicationException? Failure { get; set; }
        public Workspace? PublishedWorkspace { get; set; }
        public Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new PublicationResult(request.Workspace.RepositoryIdentity, request.Workspace.Branch, new string('a', 40), 1, "https://github.com/acme/widgets/pull/1", true, Workspace: PublishedWorkspace));
        }
        public Task<PublicationResult> PublishRevisionAsync(PublicationRevisionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PublicationResult(request.CurrentPublication.Repository, request.CurrentPublication.Branch, new string('b', 40), request.CurrentPublication.PullRequestNumber, request.CurrentPublication.PullRequestUrl, true, Workspace: request.Workspace with { LaunchNonce = "nonce-published" }));
        public Task<PublicationRecoveryResult> RecoverAsync(OwnedWorkspaceRecord record, WorkItem workItem, CancellationToken cancellationToken)
            => Task.FromResult(new PublicationRecoveryResult(false));
    }
    private sealed class FakeMutationGuard(IEnumerable<string> fingerprints) : IWorkspaceMutationGuard
    {
        private readonly Queue<string> values = new(fingerprints);
        public Task<string> CaptureAsync(Workspace workspace, CancellationToken cancellationToken) => Task.FromResult(values.Count > 0 ? values.Dequeue() : "stable");
    }
    private sealed class FakeCi(CiShepherdResult result) : ICiShepherd
    {
        public List<Workspace> Workspaces { get; } = [];
        public Task<CiShepherdResult> ShepherdAsync(CiTarget target, Workspace workspace, WorkItem item, CancellationToken token) { Workspaces.Add(workspace); return Task.FromResult(result); }
    }
    private sealed class FakeFeedback : IReviewFeedbackShepherd
    {
        public List<Workspace> Workspaces { get; } = [];
        public Task<ReviewFeedbackResult> ShepherdAsync(ReviewFeedbackTarget target, Workspace workspace, WorkItem item, CancellationToken token) { Workspaces.Add(workspace); return Task.FromResult(ReviewFeedbackResult.Success(target.HeadSha, target.PublicationRevision)); }
    }
    private sealed class FakeWorkspaceManager(Workspace workspace) : IWorkspaceManager
    {
        public List<string> Lifecycles { get; } = [];
        public List<Workspace> RecoveryInputs { get; } = [];
        public List<Workspace> LifecycleInputs { get; } = [];
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken) => Task.FromResult(workspace);
        public Task<Workspace> UpdateLifecycleAsync(Workspace value, string lifecycleStatus, CancellationToken cancellationToken) { LifecycleInputs.Add(value); Lifecycles.Add(lifecycleStatus); return Task.FromResult(value with { LifecycleStatus = lifecycleStatus }); }
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace value, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace value, DurableExecutionIdentity identity, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace value, WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken) { RecoveryInputs.Add(value); return Task.FromResult(value); }
        public Task<Workspace> UpdatePublicationStateAsync(Workspace value, PublicationRecoveryState publicationState, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([]);
        public Task CleanupTerminalAsync(Workspace value, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeCodexFactory(FakeCodexClient client) : ICodexAppServerClientFactory
    {
        public int CreateCount { get; private set; }
        public Workspace? CreatedWorkspace { get; private set; }
        public IReadOnlyDictionary<string, string>? ChildEnvironment { get; private set; }
        public ICodexAppServerClient Create(Workspace workspace, IReadOnlyDictionary<string, string>? childEnvironment = null) { CreateCount++; CreatedWorkspace = workspace; ChildEnvironment = childEnvironment; return client; }
    }
    private sealed class FakeCodexClient(Workspace workspace, IEnumerable<CodexTurnResult> scripted) : ICodexAppServerClient
    {
        private readonly Queue<CodexTurnResult> results = new(scripted);
        private Workspace current = workspace;
        public Workspace? CurrentWorkspace => current;
        public CodexProcessIdentity? ProcessIdentity => null;
        public IReadOnlyList<CodexSessionEvent> Events => [];
        public List<string> StartedThreadIds { get; } = [];
        public List<string> TurnThreadIds { get; } = [];
        public List<string> Prompts { get; } = [];
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public bool Quarantined { get; set; }
        public Exception? InitializeFailure { get; set; }
        public Exception? WaitFailure { get; set; }
        public DateTimeOffset LastActivityValue { get; set; } = DateTimeOffset.UtcNow;
        public string? LastEventTypeValue { get; set; }
        public TimeSpan IdleTimeoutValue { get; set; } = TimeSpan.FromMinutes(15);
        public bool? ProcessAliveValue { get; set; }
        public DateTimeOffset LastActivityUtc => LastActivityValue;
        public string? LastEventType => LastEventTypeValue;
        public TimeSpan IdleTimeout => IdleTimeoutValue;
        public bool? IsProcessAlive => ProcessAliveValue;
        public void AdoptWorkspace(Workspace value) => current = value;
        public int? WaitFailureAtCall { get; set; }
        private int waitCallCount;
        public Task InitializeAsync(CancellationToken cancellationToken) { current = current with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Active, LaunchNonce = "run" }; if (InitializeFailure is not null) throw InitializeFailure; return Task.CompletedTask; }
        public Task<CodexThread> StartThreadAsync(CancellationToken cancellationToken) { var id = $"thread-{StartedThreadIds.Count + 1}"; StartedThreadIds.Add(id); return Task.FromResult(new CodexThread(id)); }
        public Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken) { TurnThreadIds.Add(threadId); Prompts.Add(prompt); return Task.FromResult(new CodexTurn($"turn-{Prompts.Count}", threadId)); }
        public Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            waitCallCount++;
            if (WaitFailure is not null && (WaitFailureAtCall is null || WaitFailureAtCall == waitCallCount))
            {
                if (Quarantined) current = current with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Quarantined };
                throw WaitFailure;
            }
            return Task.FromResult(results.Dequeue() with { ThreadId = turn.ThreadId, TurnId = turn.Id });
        }
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() { DisposeCount++; Disposed = true; current = current with { ExecutionOwnershipStatus = Quarantined ? WorkspaceExecutionOwnershipStates.Quarantined : WorkspaceExecutionOwnershipStates.None }; return ValueTask.CompletedTask; }
    }
}
