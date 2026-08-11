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
    public async Task IssueExecutesInWorkspaceAndCompletedContractSucceeds()
    {
        var fixture = Create(Result("COMPLETED", "implemented and validated"));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Workspaces.Requests.Single().Repository, Is.EqualTo(new WorkspaceRepository(CanonicalRoot, "acme/widgets")));
            Assert.That(fixture.Client.Initialized, Is.True);
            Assert.That(fixture.Client.StartThreadCount, Is.EqualTo(1));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1" }));
            Assert.That(fixture.Client.Disposed, Is.True);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.Ready }));
            Assert.That(fixture.Workspaces.CleanupCount, Is.Zero);
        });
    }

    [Test]
    public async Task InitialPromptContainsBoundedTaskEnvelopeAndRepositoryInstructions()
    {
        const string trackerToken = "github-token-must-not-leak";
        var fixture = Create(Result("COMPLETED", "done"));

        await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        var prompt = fixture.Client.Prompts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("issue-42"));
            Assert.That(prompt, Does.Contain("ACME-42"));
            Assert.That(prompt, Does.Contain("Correct the widget"));
            Assert.That(prompt, Does.Contain("Full issue body"));
            Assert.That(prompt, Does.Contain(WorkspacePath.Replace("\\", "\\\\", StringComparison.Ordinal)));
            Assert.That(prompt, Does.Contain("automaton/work/work-123"));
            Assert.That(prompt, Does.Contain("acme/widgets"));
            Assert.That(prompt, Does.Contain("Repository-owned workflow guidance."));
            Assert.That(prompt, Does.Contain("root AGENTS.md"));
            Assert.That(prompt, Does.Contain("nested AGENTS.md"));
            Assert.That(prompt, Does.Contain("repository-local skills"));
            Assert.That(prompt, Does.Contain("Definition of done"));
            Assert.That(prompt, Does.Contain("self-review"));
            Assert.That(prompt, Does.Contain("AUTOMATON_RESULT"));
            Assert.That(prompt, Does.Not.Contain(CanonicalRoot));
            Assert.That(prompt, Does.Not.Contain(trackerToken));
            Assert.That(fixture.Factory.ChildEnvironment, Is.Empty);
            Assert.That(fixture.Factory.CreatedWorkspace!.Path, Is.EqualTo(WorkspacePath));
        });
    }

    [Test]
    public async Task ContinueUsesMultipleTurnsOnTheSameThread()
    {
        var fixture = Create(Result("CONTINUE", "tests remain"), Result("COMPLETED", "all done"));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.Client.StartThreadCount, Is.EqualTo(1));
            Assert.That(fixture.Client.TurnThreadIds, Is.EqualTo(new[] { "thread-1", "thread-1" }));
            Assert.That(fixture.Client.Prompts[1], Does.Contain("tests remain"));
        });
    }

    [Test]
    public async Task BlockedContractIsPermanentAndMarksWorkspaceFailed()
    {
        var fixture = Create(Result("BLOCKED", "missing required fixture"));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("missing required fixture"));
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.Failed }));
            Assert.That(fixture.Workspaces.CleanupCount, Is.Zero);
        });
    }

    [Test]
    public async Task MalformedContractDoesNotSucceed()
    {
        var fixture = Create(new CodexTurnResult("thread-1", "turn-1", "completed", FinalResponse: "Looks done."));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("malformed"));
        });
    }

    [Test]
    public async Task FailedAndInterruptedTurnsAreRetryableAndPreserveWorkspace()
    {
        foreach (var result in new[]
        {
            new CodexTurnResult("thread-1", "turn-1", "failed", "temporary model failure"),
            new CodexTurnResult("thread-1", "turn-1", "interrupted")
        })
        {
            var fixture = Create(result);
            var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(outcome.IsRetryable, Is.True);
                Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.RetryableFailure }));
                Assert.That(fixture.Workspaces.CleanupCount, Is.Zero);
                Assert.That(fixture.Client.Disposed, Is.True);
            });
        }
    }

    [Test]
    public async Task IdleTimeoutIsRetryableAndReleasesVerifiedOwnership()
    {
        var fixture = Create();
        fixture.Client.WaitFailure = new CodexIdleTimeoutException();

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(fixture.Client.Disposed, Is.True);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.None));
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.RetryableFailure }));
        });
    }

    [Test]
    public async Task MaxTurnLimitBlocksInsteadOfPretendingSuccess()
    {
        var fixture = Create(Result("CONTINUE", "one"), Result("CONTINUE", "two"));

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("Maximum autonomous turn count"));
            Assert.That(fixture.Client.Prompts, Has.Count.EqualTo(2));
            Assert.That(fixture.Workspaces.Lifecycles, Is.EqualTo(new[] { WorkspaceLifecycleStates.Failed }));
        });
    }

    [Test]
    public void CancellationDisposesClientAndPropagatesCancellation()
    {
        var fixture = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Client.WaitFailure = new OperationCanceledException(cancellation.Token);

        Assert.ThrowsAsync<OperationCanceledException>(async () => await fixture.Executor.ExecuteAsync(Attempt(), cancellation.Token));
        Assert.That(fixture.Client.Disposed, Is.True);
    }

    [Test]
    public async Task ProtocolAmbiguityKeepsQuarantineAndSkipsLifecycleMutation()
    {
        var fixture = Create();
        fixture.Client.InitializeFailure = new CodexProtocolException("malformed response");
        fixture.Client.Quarantined = true;

        var outcome = await fixture.Executor.ExecuteAsync(Attempt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(fixture.Client.CurrentWorkspace!.ExecutionOwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(fixture.Workspaces.Lifecycles, Is.Empty);
            Assert.That(fixture.Workspaces.CleanupCount, Is.Zero);
        });
    }

    [TestCase("AUTOMATON_RESULT: {\"status\":\"COMPLETED\",\"summary\":\"done\"}", "COMPLETED", true)]
    [TestCase("AUTOMATON_RESULT: {\"status\":\"CONTINUE\",\"summary\":\"more\"}", "CONTINUE", true)]
    [TestCase("AUTOMATON_RESULT: {\"status\":\"BLOCKED\",\"summary\":\"blocked\"}", "BLOCKED", true)]
    [TestCase("COMPLETED", "", false)]
    public void CompletionContractIsStrict(string response, string expectedStatus, bool valid)
    {
        var parsed = AgentCompletionContract.TryParse(response, out var completion);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.EqualTo(valid));
            if (valid) Assert.That(completion.Status, Is.EqualTo(expectedStatus));
        });
    }

    private static Fixture Create(params CodexTurnResult[] results)
    {
        var workspace = new Workspace("work-123", WorkspacePath, "automaton/work/work-123", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "reserved", "acme/widgets");
        var manager = new FakeWorkspaceManager(workspace);
        var client = new FakeCodexClient(workspace, results);
        var factory = new FakeCodexFactory(client);
        var workflow = new WorkflowDefinition("C:\\canonical-repository\\WORKFLOW.md", new Dictionary<string, object?>(), "Repository-owned workflow guidance.", new OrchestratorOptions
        {
            Repository = new RepositoryOptions { Root = CanonicalRoot, Identity = "acme/widgets" },
            Workspace = new WorkspaceOptions { Root = "C:\\isolated-workspaces" },
            Agent = new AgentExecutionOptions { MaxConcurrentAgents = 1, MaxTurns = 2 }
        });
        return new(new SingleAgentWorkExecutor(manager, factory, workflow), manager, factory, client);
    }

    private static RunAttempt Attempt() => new(
        new WorkItem("issue-42", "ACME-42", "Correct the widget", "Full issue body", "open", ["agent:ready", "bug"]),
        1,
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static CodexTurnResult Result(string status, string summary)
        => new("thread-1", Guid.NewGuid().ToString("N"), "completed", FinalResponse: $"Work report.\nAUTOMATON_RESULT: {{\"status\":\"{status}\",\"summary\":\"{summary}\"}}");

    private sealed record Fixture(SingleAgentWorkExecutor Executor, FakeWorkspaceManager Workspaces, FakeCodexFactory Factory, FakeCodexClient Client);

    private sealed class FakeWorkspaceManager(Workspace workspace) : IWorkspaceManager
    {
        public List<WorkspaceRequest> Requests { get; } = [];
        public List<string> Lifecycles { get; } = [];
        public int CleanupCount { get; private set; }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken) { Requests.Add(request); return Task.FromResult(workspace); }
        public Task<Workspace> UpdateLifecycleAsync(Workspace value, string lifecycleStatus, CancellationToken cancellationToken) { Lifecycles.Add(lifecycleStatus); return Task.FromResult(value with { LifecycleStatus = lifecycleStatus }); }
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace value, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace value, CancellationToken cancellationToken) { CleanupCount++; return Task.CompletedTask; }
    }

    private sealed class FakeCodexFactory(FakeCodexClient client) : ICodexAppServerClientFactory
    {
        public Workspace? CreatedWorkspace { get; private set; }
        public IReadOnlyDictionary<string, string>? ChildEnvironment { get; private set; }
        public ICodexAppServerClient Create(Workspace workspace, IReadOnlyDictionary<string, string>? childEnvironment = null)
        {
            CreatedWorkspace = workspace;
            ChildEnvironment = childEnvironment;
            return client;
        }
    }

    private sealed class FakeCodexClient(Workspace workspace, IEnumerable<CodexTurnResult> results) : ICodexAppServerClient
    {
        private readonly Queue<CodexTurnResult> results = new(results);
        private Workspace current = workspace;
        public Workspace? CurrentWorkspace => current;
        public CodexProcessIdentity? ProcessIdentity => null;
        public DateTimeOffset LastActivityUtc => DateTimeOffset.UtcNow;
        public IReadOnlyList<CodexSessionEvent> Events => [];
        public bool Initialized { get; private set; }
        public int StartThreadCount { get; private set; }
        public List<string> TurnThreadIds { get; } = [];
        public List<string> Prompts { get; } = [];
        public bool Disposed { get; private set; }
        public bool Quarantined { get; set; }
        public Exception? InitializeFailure { get; set; }
        public Exception? WaitFailure { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            current = current with { ExecutionOwnershipStatus = WorkspaceExecutionOwnershipStates.Active, LaunchNonce = "run" };
            if (InitializeFailure is not null) throw InitializeFailure;
            Initialized = true;
            return Task.CompletedTask;
        }
        public Task<CodexThread> StartThreadAsync(CancellationToken cancellationToken) { StartThreadCount++; return Task.FromResult(new CodexThread("thread-1")); }
        public Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken)
        {
            TurnThreadIds.Add(threadId);
            Prompts.Add(prompt);
            return Task.FromResult(new CodexTurn($"turn-{Prompts.Count}", threadId));
        }
        public Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken)
        {
            if (WaitFailure is not null) throw WaitFailure;
            return Task.FromResult(results.Dequeue());
        }
        public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            current = current with { ExecutionOwnershipStatus = Quarantined ? WorkspaceExecutionOwnershipStates.Quarantined : WorkspaceExecutionOwnershipStates.None };
            return ValueTask.CompletedTask;
        }
    }
}
