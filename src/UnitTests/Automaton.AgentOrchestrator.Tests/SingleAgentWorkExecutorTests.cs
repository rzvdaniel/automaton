using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class SingleAgentWorkExecutorTests
{
    private const string CanonicalRoot = "C:\\canonical-repository";
    private const string WorkspacePath = "C:\\isolated-workspaces\\work-123";

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

    [TestCase("CHANGES_REQUIRED")]
    [TestCase("BLOCKED")]
    public async Task NonApprovedReviewerVerdictIsPermanentManualAttention(string verdict)
    {
        var fixture = Create(ManagerReady(), DeveloperCompleted(), Reviewer(verdict, "review did not approve"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("review did not approve"));
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
    public async Task FailedRoleTurnIsRetryableAndPreservesWorkspace()
    {
        var fixture = Create(new CodexTurnResult("thread-1", "turn-1", "failed", "temporary model failure"));
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.RetryableFailure }));
            Assert.That(fixture.Client.Disposed, Is.True);
        });
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

    private static Fixture Create(params CodexTurnResult[] results) => CreateWithFingerprints([], results);

    private static Fixture CreateWithFingerprints(IEnumerable<string> fingerprints, params CodexTurnResult[] results)
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
            Agent = new AgentExecutionOptions { MaxConcurrentAgents = 1, MaxTurns = 2, ManagerMaxTurns = 2, DeveloperMaxTurns = 2, ReviewerMaxTurns = 2 }
        });
        return new(new RoleBasedWorkExecutor(manager, guard, factory, workflow), manager, factory, client);
    }

    private static RunAttempt Attempt() => new(new WorkItem("issue-42", "ACME-42", "Correct the widget", "Full issue body", "open", ["agent:ready", "bug"]), 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private static CodexTurnResult ManagerReady(string finalPrefix = "") => Manager("READY", "plan ready", finalPrefix);
    private static CodexTurnResult ManagerBlocked(string reason) => Manager("BLOCKED", reason);
    private static CodexTurnResult Manager(string status, string summary, string finalPrefix = "") => Completed($"{finalPrefix}AUTOMATON_MANAGER_RESULT: {{\"status\":\"{status}\",\"summary\":\"{summary}\",\"implementationPlan\":\"Implement widget safely\",\"validationPlan\":\"Run tests\",\"risks\":\"None\"}}");
    private static CodexTurnResult DeveloperCompleted(string finalPrefix = "") => Developer("COMPLETED", "Implementation complete", finalPrefix);
    private static CodexTurnResult DeveloperBlocked(string reason) => Developer("BLOCKED", reason);
    private static CodexTurnResult Developer(string status, string summary, string finalPrefix = "") => Completed($"{finalPrefix}AUTOMATON_DEVELOPER_RESULT: {{\"status\":\"{status}\",\"summary\":\"{summary}\",\"validation\":\"Tests passed\",\"remainingConcerns\":\"None\"}}");
    private static CodexTurnResult ReviewerApproved() => Reviewer("APPROVED", "review approved");
    private static CodexTurnResult Reviewer(string verdict, string summary, string finding = "") => Completed($"AUTOMATON_REVIEW_RESULT: {{\"verdict\":\"{verdict}\",\"summary\":\"{summary}\",\"findings\":[{{\"severity\":\"minor\",\"description\":\"{finding}\"}}]}}");
    private static CodexTurnResult Completed(string response) => new("unused", Guid.NewGuid().ToString("N"), "completed", FinalResponse: response);

    private sealed record Fixture(RoleBasedWorkExecutor Executor, FakeWorkspaceManager Workspaces, FakeCodexFactory Factory, FakeCodexClient Client);
    private sealed class FakeMutationGuard(IEnumerable<string> fingerprints) : IWorkspaceMutationGuard
    {
        private readonly Queue<string> values = new(fingerprints);
        public Task<string> CaptureAsync(Workspace workspace, CancellationToken cancellationToken) => Task.FromResult(values.Count > 0 ? values.Dequeue() : "stable");
    }
    private sealed class FakeWorkspaceManager(Workspace workspace) : IWorkspaceManager
    {
        public List<string> Lifecycles { get; } = [];
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken) => Task.FromResult(workspace);
        public Task<Workspace> UpdateLifecycleAsync(Workspace value, string lifecycleStatus, CancellationToken cancellationToken) { Lifecycles.Add(lifecycleStatus); return Task.FromResult(value with { LifecycleStatus = lifecycleStatus }); }
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace value, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace value, DurableExecutionIdentity identity, CancellationToken cancellationToken) => Task.FromResult(value);
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace value, WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken) => Task.FromResult(value);
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
        public DateTimeOffset LastActivityUtc => DateTimeOffset.UtcNow;
        public IReadOnlyList<CodexSessionEvent> Events => [];
        public List<string> StartedThreadIds { get; } = [];
        public List<string> TurnThreadIds { get; } = [];
        public List<string> Prompts { get; } = [];
        public bool Disposed { get; private set; }
        public bool Quarantined { get; set; }
        public Exception? InitializeFailure { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken) { current = current with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Active, LaunchNonce = "run" }; if (InitializeFailure is not null) throw InitializeFailure; return Task.CompletedTask; }
        public Task<CodexThread> StartThreadAsync(CancellationToken cancellationToken) { var id = $"thread-{StartedThreadIds.Count + 1}"; StartedThreadIds.Add(id); return Task.FromResult(new CodexThread(id)); }
        public Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken) { TurnThreadIds.Add(threadId); Prompts.Add(prompt); return Task.FromResult(new CodexTurn($"turn-{Prompts.Count}", threadId)); }
        public Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(results.Dequeue() with { ThreadId = turn.ThreadId, TurnId = turn.Id }); }
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; current = current with { ExecutionOwnershipStatus = Quarantined ? WorkspaceExecutionOwnershipStates.Quarantined : WorkspaceExecutionOwnershipStates.None }; return ValueTask.CompletedTask; }
    }
}
