using Automaton.AgentOrchestrator.Ci;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class CiShepherdTests
{
    private static readonly string Sha1 = new('a', 40);
    private static readonly string Sha2 = new('b', 40);
    private static readonly CiTarget Target = new("acme/widgets", 42, "automaton/work/7", Sha1);

    [Test]
    public async Task PassedExactHeadCompletesWithoutRemediation()
    {
        var fixture = Create([Snapshot(Sha1, CiStatuses.Passed)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(fixture.Remediator.Requests, Is.Empty); });
    }

    [Test]
    public async Task PendingDoesNotInvokeDeveloperAndPollingEventuallyPasses()
    {
        var fixture = Create([Snapshot(Sha1, CiStatuses.Pending), Snapshot(Sha1, CiStatuses.Passed)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(fixture.Remediator.Requests, Is.Empty); Assert.That(fixture.Delay.Count, Is.EqualTo(1)); });
    }

    [Test]
    public async Task FailureRemediatesOnceThenObservesOnlyNewSha()
    {
        var fixture = Create([Snapshot(Sha1, CiStatuses.Failed), Snapshot(Sha2, CiStatuses.Passed)], [Publication(Sha2)]);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(result.HeadSha, Is.EqualTo(Sha2)); Assert.That(fixture.Remediator.Requests, Has.Count.EqualTo(1)); Assert.That(fixture.Provider.Targets.Select(x => x.HeadSha), Is.EqualTo(new[] { Sha1, Sha2 })); });
    }

    [Test]
    public async Task FailureAfterMaximumRoundsBecomesManualAttention()
    {
        var fixture = Create([Snapshot(Sha1, CiStatuses.Failed), Snapshot(Sha2, CiStatuses.Failed)], [Publication(Sha2)], maxRounds: 1);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.False); Assert.That(result.IsRetryable, Is.False); Assert.That(fixture.Remediator.Requests, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task PendingTimeoutDoesNotInvokeRemediation()
    {
        var fixture = Create([Snapshot(Sha1, CiStatuses.Pending), Snapshot(Sha1, CiStatuses.Pending)], timeoutMs: 1);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.False); Assert.That(result.FailureReason, Does.Contain("timeout")); Assert.That(fixture.Remediator.Requests, Is.Empty); });
    }

    [Test]
    public async Task TransientProviderFailureRetriesObservationWithoutRemediation()
    {
        var provider = new FakeProvider([new CiProviderException("temporary", true), Snapshot(Sha1, CiStatuses.Passed)]);
        var fixture = Create(provider: provider);
        var result = await fixture.Shepherd.ShepherdAsync(Target, Workspace(), Work(), CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Succeeded, Is.True); Assert.That(fixture.Remediator.Requests, Is.Empty); });
    }

    private static Fixture Create(IReadOnlyList<CiSnapshot>? snapshots = null, IReadOnlyList<PublicationResult>? publications = null, int maxRounds = 2, int timeoutMs = 1000, FakeProvider? provider = null)
    {
        var clock = new FakeClock();
        var delay = new FakeDelay(clock);
        provider ??= new FakeProvider((snapshots ?? []).Cast<object>());
        var remediator = new FakeRemediator(publications ?? []);
        var options = new OrchestratorOptions { Ci = new CiOptions { Enabled = true, PollIntervalMilliseconds = 10, TimeoutMilliseconds = timeoutMs, MaxRemediationRounds = maxRounds } };
        return new(new(provider, remediator, new FakeWorkspaces(), clock, delay, options), provider, remediator, delay);
    }
    private static CiSnapshot Snapshot(string sha, string status) => new(sha, status, [new("build", status, status == CiStatuses.Failed ? "failure" : null, FailureSummary: status == CiStatuses.Failed ? "compile error" : null)]);
    private static PublicationResult Publication(string sha) => new("acme/widgets", "automaton/work/7", sha, 42, "https://github.test/pull/42", true);
    private static Workspace Workspace() => new("key", "C:/workspace", "automaton/work/7", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "acme/widgets", new string('0', 40));
    private static WorkItem Work() => new("id", "#7", "Fix", null, "open", ["agent:ready"]);
    private sealed record Fixture(CiShepherd Shepherd, FakeProvider Provider, FakeRemediator Remediator, FakeDelay Delay);
    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z"); }
    private sealed class FakeDelay(FakeClock clock) : ICiPollDelay { public int Count { get; private set; } public Task DelayAsync(TimeSpan delay, CancellationToken token) { Count++; clock.UtcNow += delay; return Task.CompletedTask; } }
    private sealed class FakeProvider(IEnumerable<object> values) : ICiProvider
    {
        private readonly Queue<object> values = new(values);
        public List<CiTarget> Targets { get; } = [];
        public Task<CiSnapshot> GetAsync(CiTarget target, CancellationToken token) { Targets.Add(target); var value = values.Dequeue(); return value is Exception error ? Task.FromException<CiSnapshot>(error) : Task.FromResult((CiSnapshot)value); }
    }
    private sealed class FakeRemediator(IEnumerable<PublicationResult> values) : ICiRemediator
    {
        private readonly Queue<PublicationResult> values = new(values);
        public List<CiRemediationRequest> Requests { get; } = [];
        public Task<PublicationResult> RemediateAsync(CiRemediationRequest request, CancellationToken token) { Requests.Add(request); return Task.FromResult(values.Dequeue()); }
    }
    private sealed class FakeWorkspaces : IWorkspaceManager
    {
        public Task<Workspace> UpdateCiStateAsync(Workspace workspace, CiRecoveryState state, CancellationToken token) => Task.FromResult(workspace);
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException();
        public Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState state, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => throw new NotSupportedException();
    }
}
