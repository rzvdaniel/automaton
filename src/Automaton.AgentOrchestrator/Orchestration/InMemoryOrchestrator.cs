using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed class InMemoryOrchestrator(
    IWorkTracker tracker,
    IWorkExecutor executor,
    OrchestratorOptions options,
    IClock clock,
    ILogger<InMemoryOrchestrator> logger) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ActiveRun> activeRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetryRun> retryRuns = new(StringComparer.Ordinal);
    private readonly List<RunState> history = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool stopping;

    public IReadOnlyList<RunState> Snapshot()
    {
        lock (gate) return [.. history, .. activeRuns.Values.Select(x => x.State), .. retryRuns.Values.Select(x => x.State)];
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (IsStopping()) return;
        var candidates = await tracker.DiscoverCandidatesAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsEligible(candidate) || !TryClaim(candidate, 1)) continue;
            StartClaim(candidate, 1);
        }
    }

    public Task CancelAsync(string workItemId)
    {
        lock (gate)
        {
            if (activeRuns.TryGetValue(workItemId, out var run)) run.Cancellation.Cancel();
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        ActiveRun[] runs;
        lock (gate)
        {
            stopping = true;
            lifetimeCancellation.Cancel();
            runs = [.. activeRuns.Values];
            foreach (var run in runs) run.Cancellation.Cancel();
        }
        await Task.WhenAll(runs.Select(x => x.Task));
    }

    private bool TryClaim(WorkItem item, int attempt)
    {
        lock (gate)
        {
            if (stopping || activeRuns.ContainsKey(item.Id) || retryRuns.ContainsKey(item.Id) || activeRuns.Count >= options.Agent.MaxConcurrentAgents) return false;
            var cancellation = new CancellationTokenSource();
            var state = new RunState(item.Id, RunStates.Claimed, attempt, clock.UtcNow);
            activeRuns[item.Id] = new ActiveRun(item, attempt, cancellation, Task.CompletedTask, state);
            return true;
        }
    }

    private void StartClaim(WorkItem item, int attempt)
    {
        Task task;
        lock (gate)
        {
            var run = activeRuns[item.Id];
            task = Task.Run(() => ExecuteClaimAsync(run));
            activeRuns[item.Id] = run with { Task = task, State = run.State with { State = RunStates.Working, UpdatedAtUtc = clock.UtcNow } };
        }
    }

    private async Task ExecuteClaimAsync(ActiveRun run)
    {
        try
        {
            var outcome = await executor.ExecuteAsync(new RunAttempt(run.WorkItem, run.Attempt, clock.UtcNow), run.Cancellation.Token);
            if (outcome.Succeeded) Complete(run.WorkItem.Id, RunStates.Completed, run.Attempt, null);
            else if (outcome.IsRetryable && run.Attempt < options.Retry.MaxAttempts && !IsStopping()) QueueRetry(run, outcome.FailureReason);
            else Complete(run.WorkItem.Id, RunStates.Blocked, run.Attempt, outcome.FailureReason);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            Complete(run.WorkItem.Id, RunStates.Cancelled, run.Attempt, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Work item {WorkItemId} execution failed", run.WorkItem.Id);
            if (run.Attempt < options.Retry.MaxAttempts && !IsStopping()) QueueRetry(run, "execution failed");
            else Complete(run.WorkItem.Id, RunStates.Blocked, run.Attempt, "execution failed");
        }
    }

    private void QueueRetry(ActiveRun run, string? failureReason)
    {
        var delay = RetryDelay(run.Attempt);
        lock (gate)
        {
            if (!activeRuns.Remove(run.WorkItem.Id)) return;
            var retryState = new RunState(run.WorkItem.Id, RunStates.RetryQueued, run.Attempt, clock.UtcNow, clock.UtcNow.Add(delay), failureReason);
            history.Add(retryState);
            retryRuns[run.WorkItem.Id] = new RetryRun(run.WorkItem, run.Attempt + 1, retryState);
            _ = RetryWhenDueAsync(run.WorkItem, run.Attempt + 1, delay);
        }
    }

    private async Task RetryWhenDueAsync(WorkItem item, int attempt, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, lifetimeCancellation.Token);
            lock (gate) retryRuns.Remove(item.Id);
            if (!IsStopping() && TryClaim(item, attempt)) StartClaim(item, attempt);
        }
        catch (OperationCanceledException) { }
    }

    private void Complete(string workItemId, string state, int attempt, string? failureReason)
    {
        lock (gate)
        {
            activeRuns.Remove(workItemId);
            history.Add(new RunState(workItemId, state, attempt, clock.UtcNow, null, failureReason));
        }
    }

    private TimeSpan RetryDelay(int completedAttempt)
    {
        var multiplier = 1L << Math.Min(completedAttempt - 1, 30);
        return TimeSpan.FromMilliseconds(Math.Min((long)options.Retry.InitialBackoffMilliseconds * multiplier, options.Retry.MaxBackoffMilliseconds));
    }

    private bool IsEligible(WorkItem item)
        => options.Tracker.ActiveProviderLifecycleStates.Contains(item.ProviderLifecycleState, StringComparer.OrdinalIgnoreCase)
           && options.Tracker.RequiredLabels.All(required => item.Labels.Contains(required, StringComparer.OrdinalIgnoreCase));

    private bool IsStopping() { lock (gate) return stopping; }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        lifetimeCancellation.Dispose();
    }

    private sealed record ActiveRun(WorkItem WorkItem, int Attempt, CancellationTokenSource Cancellation, Task Task, RunState State);
    private sealed record RetryRun(WorkItem WorkItem, int NextAttempt, RunState State);
}
