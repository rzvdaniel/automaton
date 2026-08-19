using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class SupervisedCodexWorkExecutorTests
{
    [TestCase("APPROVED")]
    [TestCase("CHANGES_REQUIRED")]
    [TestCase("BLOCKED")]
    public void ReviewContractIsStrictAndBounded(string verdict)
    {
        var response = $"REVIEW_RESULT: {{\"verdict\":\"{verdict}\",\"summary\":\"summary\"}}";
        Assert.That(ReviewTerminalContract.TryParse(response, out _), Is.True);
        Assert.That(ReviewTerminalContract.TryParse("REVIEW_RESULT: APPROVED", out _), Is.False);
    }

    [TestCase(null, ReviewTerminalParseFailure.EmptyResponse)]
    [TestCase("plain prose", ReviewTerminalParseFailure.MissingTerminalLine)]
    [TestCase("REVIEW_RESULT: {", ReviewTerminalParseFailure.InvalidJson)]
    [TestCase("REVIEW_RESULT: {\"summary\":\"x\"}", ReviewTerminalParseFailure.MissingVerdict)]
    [TestCase("REVIEW_RESULT: {\"verdict\":\"NOPE\",\"summary\":\"x\"}", ReviewTerminalParseFailure.UnsupportedVerdict)]
    [TestCase("REVIEW_RESULT: {\"verdict\":\"APPROVED\"}", ReviewTerminalParseFailure.MissingSummary)]
    public void ReviewContractReportsBoundedParseFailure(string? response, ReviewTerminalParseFailure expected)
    {
        Assert.That(ReviewTerminalContract.TryParse(response, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(expected));
    }

    [Test]
    public void ReviewContractReportsOverlongSummaryWithoutLeakingContents()
    {
        var secret = new string('x', 513);
        var response = $"REVIEW_RESULT: {{\"verdict\":\"APPROVED\",\"summary\":\"{secret}\"}}";
        Assert.That(ReviewTerminalContract.TryParse(response, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(ReviewTerminalParseFailure.SummaryTooLong));
        Assert.That(failure.ToString(), Does.Not.Contain(secret));
    }
    [TestCase("COMPLETED", true, false)]
    [TestCase("BLOCKED", false, false)]
    [TestCase("RETRYABLE", false, true)]
    public async Task TerminalContractMapsToSupervisorOutcome(string status, bool succeeded, bool retryable)
    {
        var fixture = Create(status);
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.EqualTo(succeeded));
            Assert.That(outcome.IsRetryable, Is.EqualTo(retryable));
            Assert.That(fixture.Client.InitializeCount, Is.EqualTo(1));
            Assert.That(fixture.Factory.Context!.Workspace.Path, Is.EqualTo(fixture.Workspace.Path));
            Assert.That(fixture.Client.Prompt, Does.Contain("Acceptance task"));
            Assert.That(fixture.Client.Prompt, Does.Contain("WORKFLOW policy"));
            Assert.That(fixture.Workspaces.FinalWorkspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
        });
    }

    [Test]
    public async Task RuntimeFailureReleasesOwnershipAndBecomesRetryable()
    {
        var fixture = Create("COMPLETED");
        fixture.Client.Failure = new InvalidOperationException("runtime failed");
        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(outcome.IsRetryable, Is.True); Assert.That(fixture.Client.DisposeCount, Is.EqualTo(1)); Assert.That(fixture.Workspaces.FinalWorkspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None)); });
    }

    [Test]
    public void CancellationReleasesOwnershipBeforeItPropagates()
    {
        var fixture = Create("COMPLETED");
        using var cancellation = new CancellationTokenSource();
        fixture.Client.CancelSource = cancellation;

        Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Executor.ExecuteAsync(Attempt(), cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Client.DisposeCount, Is.EqualTo(1));
            Assert.That(fixture.Factory.Context!.Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
            Assert.That(fixture.Workspaces.LifecycleWrites, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task EnabledGitHubCapability_InjectsOnlyTheExplicitCredentialAndWorkflowInstructions()
    {
        var fixture = Create("COMPLETED", new AgentGitHubOptions { Enabled = true, Token = "test-agent-token" });

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Factory.Environment, Is.EqualTo(new Dictionary<string, string> { ["GH_TOKEN"] = "test-agent-token" }));
            Assert.That(fixture.Client.Prompt, Does.Contain("commit the completed change"));
            Assert.That(fixture.Client.Prompt, Does.Contain("create or update one draft pull request"));
            Assert.That(fixture.Client.Prompt, Does.Contain("authenticated `gh` CLI"));
            Assert.That(fixture.Client.Prompt, Does.Contain("Never merge or approve"));
            Assert.That(fixture.Client.Prompt, Does.Not.Contain("test-agent-token"));
        });
    }

    [Test]
    public async Task DisabledGitHubCapability_DoesNotInjectCredentialsOrPermitPublication()
    {
        var fixture = Create("COMPLETED");

        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Factory.Environment, Is.Empty);
            Assert.That(fixture.Client.Prompt, Does.Contain("Repository workflow capability is not enabled"));
        });
    }

    [Test]
    public async Task DeveloperProfileModelAndPermissionsReachGenericRuntime()
    {
        var fixture = Create("COMPLETED");
        var profile = new AgentExecutionProfile("developer", "model-a", "read-write", "write", "developer instructions");
        var workflow = new WorkflowDefinition("WORKFLOW.md", new Dictionary<string, object?>(), "WORKFLOW policy", new OrchestratorOptions
        {
            Repository = new() { Root = "C:\\repo", Identity = "acme/repo" },
            Agent = new() { MaxTurns = 2, Developer = profile }
        });
        var executor = new SupervisedCodexWorkExecutor(fixture.Workspaces, fixture.Factory, workflow);
        var outcome = await executor.ExecuteAsync(Attempt(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Factory.Profile, Is.EqualTo(profile));
            Assert.That(fixture.Client.Prompt, Does.Contain("model: model-a"));
            Assert.That(fixture.Client.Prompt, Does.Contain("developer instructions"));
        });
    }

    [Test]
    public async Task TurnTimeoutRecordsBoundedTelemetryAndRemainsRetryable()
    {
        var telemetry = new RecordingTelemetry();
        var fixture = Create("COMPLETED", telemetry: telemetry);
        fixture.Client.Failure = new CodexTurnTimeoutException(new("turn", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow, "turn/started", true));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        var eventRecord = telemetry.Events.Single(x => x.EventType == "agent.codex.timeout");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(eventRecord.Metadata!["timeoutKind"], Is.EqualTo("turn"));
            Assert.That(eventRecord.Metadata["configuredTimeoutMs"], Is.EqualTo("60000"));
            Assert.That(eventRecord.Metadata["totalElapsedMs"], Is.EqualTo("60000"));
            Assert.That(eventRecord.Metadata["lastEventType"], Is.EqualTo("turn/started"));
            Assert.That(eventRecord.Metadata["processAlive"], Is.EqualTo("True"));
            Assert.That(eventRecord.Metadata.Values, Has.None.Contains("secret"));
            Assert.That(fixture.Workspaces.FinalWorkspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
        });
    }

    [Test]
    public async Task StallTimeoutRecordsInactivityTelemetryAndRemainsRetryable()
    {
        var telemetry = new RecordingTelemetry();
        var fixture = Create("COMPLETED", telemetry: telemetry);
        fixture.Client.Failure = new CodexIdleTimeoutException(new("stall", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(30), DateTimeOffset.UtcNow, "item/completed", false));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
        var eventRecord = telemetry.Events.Single(x => x.EventType == "agent.codex.timeout");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(eventRecord.Metadata!["timeoutKind"], Is.EqualTo("stall"));
            Assert.That(eventRecord.Metadata["configuredTimeoutMs"], Is.EqualTo("30000"));
            Assert.That(eventRecord.Metadata["inactivityMs"], Is.EqualTo("30000"));
            Assert.That(eventRecord.Metadata["totalElapsedMs"], Is.EqualTo("42000"));
            Assert.That(eventRecord.Metadata["lastEventType"], Is.EqualTo("item/completed"));
            Assert.That(eventRecord.Metadata["processAlive"], Is.EqualTo("False"));
        });
    }

    private static Fixture Create(string status, AgentGitHubOptions? github = null, RecordingTelemetry? telemetry = null)
    {
        var workspace = new Workspace("work-1", "C:\\work\\work-1", "automaton/work/work-1", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce-a", "acme/repo", new string('a', 40));
        var manager = new FakeWorkspaces(workspace);
        var client = new FakeClient(status);
        var workflow = new WorkflowDefinition("WORKFLOW.md", new Dictionary<string, object?>(), "WORKFLOW policy", new OrchestratorOptions { Repository = new() { Root = "C:\\repo", Identity = "acme/repo" }, Agent = new() { MaxTurns = 2, GitHub = github ?? new() } });
        var factory = new FakeFactory(client);
        return new(new SupervisedCodexWorkExecutor(manager, factory, workflow, telemetry), manager, client, workspace, factory);
    }

    private static RunAttempt Attempt() => new(new WorkItem("id-1", "#1", "Acceptance task", "Implement the task", "open", ["ready"]), 1, DateTimeOffset.UtcNow);
    private sealed record Fixture(SupervisedCodexWorkExecutor Executor, FakeWorkspaces Workspaces, FakeClient Client, Workspace Workspace, FakeFactory Factory);
    private sealed class RecordingTelemetry : IOperatorTelemetry
    {
        public List<OperatorEvent> Events { get; } = [];
        public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null)
            => Events.Add(new(Events.Count + 1, DateTimeOffset.UtcNow, eventType, severity, workItemId, stage, role, message, metadata));
        public OperatorEvent? LatestFor(string workItemId) => Events.LastOrDefault(x => x.WorkItemId == workItemId);
        public void SetDaemonState(string state, string? error = null) { }
        public (string State, string? LastError) DaemonState() => (OperatorDaemonStates.Running, null);
    }
    private sealed class FakeFactory(FakeClient client) : ICodexAppServerClientFactory
    {
        public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();
        public WorkspaceExecutionContext? Context { get; private set; }
        public AgentExecutionProfile? Profile { get; private set; }
        public ICodexAppServerClient Create(WorkspaceExecutionContext context, IReadOnlyDictionary<string, string>? environment = null)
        {
            Environment = new Dictionary<string, string>(environment ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            Context = context;
            client.Context = context;
            return client;
        }
        public ICodexAppServerClient Create(WorkspaceExecutionContext context, AgentExecutionProfile profile, IReadOnlyDictionary<string, string>? environment = null)
        {
            Profile = profile;
            return Create(context, environment);
        }
    }
    private sealed class FakeClient(string status) : ICodexAppServerClient
    {
        public WorkspaceExecutionContext? Context { get; set; }
        public int InitializeCount { get; private set; }
        public int DisposeCount { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public Exception? Failure { get; set; }
        public CancellationTokenSource? CancelSource { get; set; }
        public AgentExecutionProfile? Profile { get; private set; }
        public CodexProcessIdentity? ProcessIdentity => null; public DateTimeOffset LastActivityUtc => DateTimeOffset.UtcNow; public IReadOnlyList<CodexSessionEvent> Events => [];
        public async Task InitializeAsync(CancellationToken token)
        {
            InitializeCount++;
            await Context!.SetOwnershipAsync(WorkspaceExecutionOwnershipStates.Active, "nonce-active", token);
        }
        public Task<CodexThread> StartThreadAsync(CancellationToken token) => Task.FromResult(new CodexThread("thread-1"));
        public Task<CodexThread> ResumeThreadAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken token) { Prompt = prompt; return Task.FromResult(new CodexTurn("turn-1", threadId)); }
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, AgentExecutionProfile profile, CancellationToken token) { Profile = profile; return StartTurnAsync(threadId, prompt, token); }
        public Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken token) { if (CancelSource is not null) { CancelSource.Cancel(); throw new OperationCanceledException(token); } if (Failure is not null) throw Failure; return Task.FromResult(new CodexTurnResult(turn.ThreadId, turn.Id, "completed", FinalResponse: $"AUTOMATON_RESULT: {{\"status\":\"{status}\",\"summary\":\"done\"}}")); }
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken token) => Task.CompletedTask;
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            await Context!.SetOwnershipAsync(WorkspaceExecutionOwnershipStates.None, Context.Workspace.LaunchNonce, CancellationToken.None);
        }
    }
    private sealed class FakeWorkspaces(Workspace workspace) : IWorkspaceManager
    {
        public Workspace FinalWorkspace { get; private set; } = workspace;
        public int LifecycleWrites { get; private set; }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => Task.FromResult(FinalWorkspace);
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace value, WorkspaceRecoveryState state, CancellationToken token) => Task.FromResult(FinalWorkspace = RequireCurrent(value));
        public Task<Workspace> UpdateLifecycleAsync(Workspace value, string state, CancellationToken token) { LifecycleWrites++; return Task.FromResult(FinalWorkspace = RequireCurrent(value) with { LifecycleStatus = state }); }
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace value, string state, string nonce, CancellationToken token) => Task.FromResult(FinalWorkspace = RequireCurrent(value) with { ExecutionOwnershipStatus = state, LaunchNonce = nonce });
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace value, DurableExecutionIdentity identity, CancellationToken token) => Task.FromResult(RequireCurrent(value));
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([]);
        public Task CleanupTerminalAsync(Workspace value, CancellationToken token) => Task.CompletedTask;

        private Workspace RequireCurrent(Workspace value)
        {
            if (!string.Equals(value.LaunchNonce, FinalWorkspace.LaunchNonce, StringComparison.Ordinal))
                throw new WorkspaceRecoveryException("Workspace does not match daemon-owned sidecar state.");
            return value;
        }
    }
}
