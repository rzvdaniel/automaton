using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Operator;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class OperatorTelemetryTests
{
    [Test]
    public void EventsAreBoundedStructuredAndSensitiveMetadataIsRedacted()
    {
        using var temp = new TempDirectory();
        var telemetry = Create(temp.Path);
        telemetry.Record("run.test", OperatorEventSeverities.Warning, "issue-1", "ci", "developer", new string('x', 2000), new Dictionary<string, string> { ["token"] = "raw-secret", ["headSha"] = new string('a', 40) });
        var json = File.ReadAllText(Path.Combine(telemetry.RootPath, "events.jsonl"));
        using var document = JsonDocument.Parse(json.Trim());
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("message").GetString()!.Length, Is.EqualTo(512));
            Assert.That(document.RootElement.GetProperty("metadata").GetProperty("token").GetString(), Is.EqualTo("[REDACTED]"));
            Assert.That(json, Does.Not.Contain("raw-secret"));
            Assert.That(document.RootElement.GetProperty("stage").GetString(), Is.EqualTo("ci"));
        });
    }

    [Test]
    public void EventFileRotatesWithinDaemonOwnedOperatorDirectory()
    {
        using var temp = new TempDirectory();
        var telemetry = Create(temp.Path, maximumBytes: 200);
        telemetry.Record("one", OperatorEventSeverities.Information, message: new string('a', 180));
        telemetry.Record("two", OperatorEventSeverities.Information, message: new string('b', 180));
        Assert.Multiple(() => { Assert.That(File.Exists(Path.Combine(telemetry.RootPath, "events.jsonl")), Is.True); Assert.That(File.Exists(Path.Combine(telemetry.RootPath, "events.jsonl.1")), Is.True); });
    }

    [Test]
    public async Task StatusSnapshotCombinesSchedulerAndDurableWorkspaceIdentity()
    {
        using var temp = new TempDirectory();
        var options = Options(temp.Path);
        var telemetry = new FileOperatorTelemetry(options, new FixedClock());
        var tracker = new EmptyTracker();
        var orchestrator = new InMemoryOrchestrator(tracker, new NoopExecutor(), options, new FixedClock(), NullLogger<InMemoryOrchestrator>.Instance, telemetry);
        orchestrator.RestoreTerminalState("issue-1", RunStates.Blocked, 2, "manual attention");
        orchestrator.CompleteStartupReconciliation();
        telemetry.Record("feedback.snapshot", OperatorEventSeverities.Warning, "issue-1", "human-feedback", message: "actionable");
        var record = new OwnedWorkspaceRecord(new("work-key", Path.Combine(temp.Path, "work-key"), "automaton/work/work-key", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "acme/widgets"), "issue-1",
            new(temp.Path, "acme/widgets"), new(WorkspaceRecoveryStates.Blocked, 2, 3, Reason: "manual attention"), null, 7);
        var service = new OperatorStatusHostedService(orchestrator, new FakeWorkspaces(record), telemetry, new FixedClock(), options, NullLogger<OperatorStatusHostedService>.Instance);
        await service.WriteOnceAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(telemetry.StatusPath));
        var run = document.RootElement.GetProperty("runs")[0];
        Assert.Multiple(() => { Assert.That(document.RootElement.GetProperty("healthy").GetBoolean(), Is.True); Assert.That(run.GetProperty("stage").GetString(), Is.EqualTo("human-feedback")); Assert.That(run.GetProperty("ownershipStatus").GetString(), Is.EqualTo(WorkspaceExecutionOwnershipStates.None)); Assert.That(run.TryGetProperty("ciStatus", out _), Is.False); });
        await orchestrator.DisposeAsync();
    }

    private static FileOperatorTelemetry Create(string root, int maximumBytes = 5000) => new(Options(root, maximumBytes), new FixedClock());
    private static OrchestratorOptions Options(string root, int maximumBytes = 5000) => new() { Workspace = new WorkspaceOptions { Root = root }, Repository = new RepositoryOptions { Root = root, Identity = "acme/widgets" }, Observability = new ObservabilityOptions { Enabled = true, MaximumEventFileBytes = maximumBytes, SnapshotIntervalMilliseconds = 10 }, Tracker = new TrackerOptions { ActiveProviderLifecycleStates = ["open"] } };
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-12T00:00:00Z"); }
    private sealed class EmptyTracker : IWorkTracker { public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<WorkItem>>([]); public Task<WorkItem?> GetByIdAsync(string id, CancellationToken token) => Task.FromResult<WorkItem?>(null); }
    private sealed class NoopExecutor : IWorkExecutor { public Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken token) => Task.FromResult(RunOutcome.Success()); }
    private sealed class FakeWorkspaces(OwnedWorkspaceRecord record) : IWorkspaceManager
    {
        public Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken token) => Task.FromResult<IReadOnlyList<OwnedWorkspaceRecord>>([record]);
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string state, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string state, string nonce, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken token) => throw new NotSupportedException(); public Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState state, CancellationToken token) => throw new NotSupportedException(); public Task CleanupTerminalAsync(Workspace workspace, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class TempDirectory : IDisposable { public TempDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automaton-operator-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); } public string Path { get; } public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); } }
}
