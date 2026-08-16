using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class StartupReconcilerTests
{
    [Test]
    public async Task DeadOwnedExecutionIsReleasedAndAnIncompleteRunIsBlocked()
    {
        var record = Record(WorkspaceExecutionOwnershipStates.Active, WorkspaceRecoveryStates.Working,
            new DurableExecutionIdentity(42, DateTimeOffset.UtcNow, "nonce-a", ExecutionFencingKinds.WindowsJob));
        var fixture = Create(record, ExecutionIdentityClassifications.Dead);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Workspaces.OwnershipUpdates, Is.EqualTo(new[] { WorkspaceExecutionOwnershipStates.None }));
            Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.True);
            Assert.That(fixture.Orchestrator.Snapshot().Single().State, Is.EqualTo(RunStates.Blocked));
        });
    }

    [Test]
    public async Task AmbiguousActiveOwnershipIsQuarantined()
    {
        var record = Record(WorkspaceExecutionOwnershipStates.Active, WorkspaceRecoveryStates.Working, null);
        var fixture = Create(record, ExecutionIdentityClassifications.Ambiguous);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Workspaces.OwnershipUpdates, Is.EqualTo(new[] { WorkspaceExecutionOwnershipStates.Quarantined }));
            Assert.That(fixture.Orchestrator.Snapshot().Single().State, Is.EqualTo(RunStates.Blocked));
        });
    }

    [Test]
    public async Task EligibleRetryableFailureRestoresRetryWithoutGitHubWorkflowServices()
    {
        var record = Record(WorkspaceExecutionOwnershipStates.None, WorkspaceRecoveryStates.RetryableFailure, null) with
        {
            Recovery = new WorkspaceRecoveryState(WorkspaceRecoveryStates.RetryableFailure, 1, 2, Reason: "transient")
        };
        var fixture = Create(record, ExecutionIdentityClassifications.Dead);

        await fixture.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Orchestrator.IsStartupReconciled, Is.True);
            Assert.That(fixture.Orchestrator.Snapshot(), Has.Some.Property(nameof(RunState.State)).EqualTo(RunStates.RetryQueued));
        });
    }

    private static Fixture Create(OwnedWorkspaceRecord record, string classification)
    {
        var options = new OrchestratorOptions
        {
            Repository = new() { Root = "C:\\repo", Identity = "acme/widgets", BaseBranch = "main" },
            Workspace = new() { Root = "C:\\workspaces" },
            Tracker = new() { Kind = "fake", RequiredLabels = ["agent:ready"], ActiveProviderLifecycleStates = ["open"] },
            Retry = new() { MaxAttempts = 3 }
        };
        var tracker = new FakeTracker();
        var executor = new NoopExecutor();
        var orchestrator = new InMemoryOrchestrator(tracker, executor, options, new SystemClock(), NullLogger<InMemoryOrchestrator>.Instance);
        var workspaces = new FakeWorkspaces(record);
        return new(new StartupReconciler(workspaces, tracker, new FakeFence(classification), orchestrator, options, NullLogger<StartupReconciler>.Instance), workspaces, orchestrator);
    }

    private static OwnedWorkspaceRecord Record(string ownership, string recovery, DurableExecutionIdentity? identity)
    {
        var workspace = new Workspace("work-key", "C:\\workspaces\\work-key", "automaton/work/work-key", WorkspaceLifecycleStates.Ready,
            ownership, "nonce-a", "acme/widgets", new string('a', 40));
        return new(workspace, "issue-1", new("C:\\repo", "acme/widgets", "main"), new(recovery, 1, 2), identity, 7);
    }

    private sealed record Fixture(StartupReconciler Reconciler, FakeWorkspaces Workspaces, InMemoryOrchestrator Orchestrator);
    private sealed class FakeTracker : IWorkTracker
    {
        private readonly WorkItem item = new("issue-1", "#1", "Task", "", "open", ["agent:ready"]);
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<WorkItem>>([item]);
        public Task<WorkItem?> GetByIdAsync(string id, CancellationToken token) => Task.FromResult<WorkItem?>(id == item.Id ? item : null);
    }
    private sealed class NoopExecutor : IWorkExecutor { public Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken token) => Task.FromResult(RunOutcome.Success()); }
    private sealed class FakeFence(string classification) : IExecutionFence
    {
        public Task<FencedProcess> LaunchAsync(System.Diagnostics.ProcessStartInfo info, string nonce, CancellationToken token) => throw new NotSupportedException();
        public Task<ExecutionIdentityInspection> InspectAsync(DurableExecutionIdentity identity, CancellationToken token) => Task.FromResult(new ExecutionIdentityInspection(classification));
        public Task<bool> TerminateAndVerifyAsync(DurableExecutionIdentity identity, TimeSpan timeout, CancellationToken token) => Task.FromResult(true);
    }
    private sealed class FakeWorkspaces(OwnedWorkspaceRecord record) : IWorkspaceManager
    {
        public List<string> OwnershipUpdates { get; } = [];
        private OwnedWorkspaceRecord record = record;
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([record]);
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token)
        {
            OwnershipUpdates.Add(state);
            var updated = workspace with { ExecutionOwnershipStatus = state };
            record = record with { Workspace = updated, ExecutionIdentity = state == WorkspaceExecutionOwnershipStates.None ? null : record.ExecutionIdentity };
            return Task.FromResult(updated);
        }
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => Task.CompletedTask;
    }
}
