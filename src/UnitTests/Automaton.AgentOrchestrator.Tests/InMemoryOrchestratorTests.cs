using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class InMemoryOrchestratorTests
{
    [Test]
    public async Task PollAsync_DispatchesOnlyEligibleProviderOpenWork()
    {
        var executor = new QueueExecutor(RunOutcome.Success());
        await using var orchestrator = Create(
            new FakeTracker(Work("eligible"), Work("closed", "closed"), Work("unlabeled", labels: [])), executor);

        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => executor.Attempts.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Attempts.Single().WorkItem.Id, Is.EqualTo("eligible"));
            Assert.That(executor.Attempts.Single().WorkItem.ProviderLifecycleState, Is.EqualTo("open"));
            Assert.That(orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Completed && x.WorkItemId == "eligible"));
        });
    }

    [Test]
    public async Task PollAsync_EnforcesBoundedConcurrency()
    {
        var executor = new BlockingExecutor();
        await using var orchestrator = Create(new FakeTracker(Work("one"), Work("two")), executor, maxConcurrent: 1);

        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => executor.Attempts.Count == 1);
        Assert.That(orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Working));

        executor.Release(RunOutcome.Success());
        await EventuallyAsync(() => orchestrator.Snapshot().Any(x => x.State == RunStates.Completed));
        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => executor.Attempts.Count == 2);
    }

    [Test]
    public async Task CancelAsync_CancelsActiveExecutionAndRecordsTransientCancelledState()
    {
        var executor = new BlockingExecutor();
        await using var orchestrator = Create(new FakeTracker(Work("one")), executor);

        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => executor.Attempts.Count == 1);
        await orchestrator.CancelAsync("one");

        await EventuallyAsync(() => orchestrator.Snapshot().Any(x => x.WorkItemId == "one" && x.State == RunStates.Cancelled));
        Assert.That(executor.WasCancelled, Is.True);
    }

    [Test]
    public async Task RetryableFailure_RequeuesWithIncrementedAttempt()
    {
        var executor = new QueueExecutor(RunOutcome.RetryableFailure("temporary"), RunOutcome.Success());
        await using var orchestrator = Create(new FakeTracker(Work("one")), executor, initialBackoffMilliseconds: 100);

        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => orchestrator.Snapshot().Any(x => x.WorkItemId == "one" && x.State == RunStates.RetryQueued));
        await orchestrator.PollAsync(CancellationToken.None);
        await Task.Delay(20);
        Assert.That(executor.Attempts, Has.Count.EqualTo(1), "retry-queued work remains claimed until its retry is due");
        await EventuallyAsync(() => executor.Attempts.Count == 2);
        await EventuallyAsync(() => orchestrator.Snapshot().Any(x => x.WorkItemId == "one" && x.State == RunStates.Completed && x.Attempt == 2));

        Assert.That(orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.WorkItemId == "one" && x.State == RunStates.RetryQueued && x.Attempt == 1));
    }

    [Test]
    public async Task StopAsync_CancelsActiveWorkAndPreventsFutureDispatch()
    {
        var executor = new BlockingExecutor();
        await using var orchestrator = Create(new FakeTracker(Work("one")), executor);

        await orchestrator.PollAsync(CancellationToken.None);
        await EventuallyAsync(() => executor.Attempts.Count == 1);
        await orchestrator.StopAsync();
        await orchestrator.PollAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(executor.WasCancelled, Is.True);
            Assert.That(executor.Attempts, Has.Count.EqualTo(1));
            Assert.That(orchestrator.Snapshot(), Has.Some.Matches<RunState>(x => x.State == RunStates.Cancelled));
        });
    }

    private static InMemoryOrchestrator Create(FakeTracker tracker, IWorkExecutor executor, int maxConcurrent = 2, int initialBackoffMilliseconds = 10)
        => new(tracker, executor, new OrchestratorOptions
        {
            Tracker = new TrackerOptions { Kind = "fake", RequiredLabels = ["agent:ready"], ActiveProviderLifecycleStates = ["open"], TerminalProviderLifecycleStates = ["closed"] },
            Agent = new AgentExecutionOptions { MaxConcurrentAgents = maxConcurrent, MaxTurns = 1 },
            Retry = new RetryOptions { MaxAttempts = 3, InitialBackoffMilliseconds = initialBackoffMilliseconds, MaxBackoffMilliseconds = 100 }
        }, new FixedClock(), NullLogger<InMemoryOrchestrator>.Instance);

    private static WorkItem Work(string id, string providerLifecycleState = "open", IReadOnlyList<string>? labels = null)
        => new(id, $"FAKE-{id}", id, null, providerLifecycleState, labels ?? ["agent:ready"]);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for expected orchestrator state.");
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed class FakeTracker(params WorkItem[] workItems) : IWorkTracker
    {
        public Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkItem>>(workItems);
        public Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken) => Task.FromResult(workItems.SingleOrDefault(x => x.Id == workItemId));
    }

    private sealed class QueueExecutor(params RunOutcome[] outcomes) : IWorkExecutor
    {
        private readonly Queue<RunOutcome> outcomes = new(outcomes);
        public List<RunAttempt> Attempts { get; } = [];
        public Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken)
        {
            Attempts.Add(attempt);
            return Task.FromResult(outcomes.Dequeue());
        }
    }

    private sealed class BlockingExecutor : IWorkExecutor
    {
        private readonly TaskCompletionSource<RunOutcome> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<RunAttempt> Attempts { get; } = [];
        public bool WasCancelled { get; private set; }

        public async Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken)
        {
            Attempts.Add(attempt);
            try { return await release.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { WasCancelled = true; throw; }
        }

        public void Release(RunOutcome outcome) => release.TrySetResult(outcome);
    }
}
