using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class SupervisedCodexWorkExecutorTests
{
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
            Assert.That(fixture.Client.Workspace.Path, Is.EqualTo(fixture.Workspace.Path));
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
            Assert.That(fixture.Client.Workspace.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
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
            Assert.That(fixture.Client.Prompt, Does.Contain("publication capability is not enabled"));
        });
    }

    private static Fixture Create(string status, AgentGitHubOptions? github = null)
    {
        var workspace = new Workspace("work-1", "C:\\work\\work-1", "automaton/work/work-1", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce-a", "acme/repo", new string('a', 40));
        var manager = new FakeWorkspaces(workspace);
        var client = new FakeClient(workspace, status);
        var workflow = new WorkflowDefinition("WORKFLOW.md", new Dictionary<string, object?>(), "WORKFLOW policy", new OrchestratorOptions { Repository = new() { Root = "C:\\repo", Identity = "acme/repo" }, Agent = new() { MaxTurns = 2, GitHub = github ?? new() } });
        var factory = new FakeFactory(client);
        return new(new SupervisedCodexWorkExecutor(manager, factory, workflow), manager, client, workspace, factory);
    }

    private static RunAttempt Attempt() => new(new WorkItem("id-1", "#1", "Acceptance task", "Implement the task", "open", ["ready"]), 1, DateTimeOffset.UtcNow);
    private sealed record Fixture(SupervisedCodexWorkExecutor Executor, FakeWorkspaces Workspaces, FakeClient Client, Workspace Workspace, FakeFactory Factory);
    private sealed class FakeFactory(FakeClient client) : ICodexAppServerClientFactory
    {
        public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();
        public ICodexAppServerClient Create(Workspace workspace, IReadOnlyDictionary<string, string>? environment = null)
        {
            Environment = new Dictionary<string, string>(environment ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            client.Workspace = workspace;
            return client;
        }
    }
    private sealed class FakeClient(Workspace workspace, string status) : ICodexAppServerClient
    {
        public Workspace Workspace { get; set; } = workspace;
        public Workspace? CurrentWorkspace => Workspace;
        public int InitializeCount { get; private set; }
        public int DisposeCount { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public Exception? Failure { get; set; }
        public CancellationTokenSource? CancelSource { get; set; }
        public CodexProcessIdentity? ProcessIdentity => null; public DateTimeOffset LastActivityUtc => DateTimeOffset.UtcNow; public IReadOnlyList<CodexSessionEvent> Events => [];
        public Task InitializeAsync(CancellationToken token) { InitializeCount++; Workspace = Workspace with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Active }; return Task.CompletedTask; }
        public Task<CodexThread> StartThreadAsync(CancellationToken token) => Task.FromResult(new CodexThread("thread-1"));
        public Task<CodexThread> ResumeThreadAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken token) { Prompt = prompt; return Task.FromResult(new CodexTurn("turn-1", threadId)); }
        public Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken token) { if (CancelSource is not null) { CancelSource.Cancel(); throw new OperationCanceledException(token); } if (Failure is not null) throw Failure; return Task.FromResult(new CodexTurnResult(turn.ThreadId, turn.Id, "completed", FinalResponse: $"AUTOMATON_RESULT: {{\"status\":\"{status}\",\"summary\":\"done\"}}")); }
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() { DisposeCount++; Workspace = Workspace with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.None }; return ValueTask.CompletedTask; }
    }
    private sealed class FakeWorkspaces(Workspace workspace) : IWorkspaceManager
    {
        public Workspace FinalWorkspace { get; private set; } = workspace;
        public int LifecycleWrites { get; private set; }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => Task.FromResult(FinalWorkspace);
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace value, WorkspaceRecoveryState state, CancellationToken token) => Task.FromResult(FinalWorkspace = value);
        public Task<Workspace> UpdateLifecycleAsync(Workspace value, string state, CancellationToken token) { LifecycleWrites++; return Task.FromResult(FinalWorkspace = value with { LifecycleStatus = state }); }
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace value, string state, string nonce, CancellationToken token) => Task.FromResult(FinalWorkspace = value with { ExecutionOwnershipStatus = state, LaunchNonce = nonce });
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace value, DurableExecutionIdentity identity, CancellationToken token) => Task.FromResult(value);
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([]);
        public Task CleanupTerminalAsync(Workspace value, CancellationToken token) => Task.CompletedTask;
    }
}
