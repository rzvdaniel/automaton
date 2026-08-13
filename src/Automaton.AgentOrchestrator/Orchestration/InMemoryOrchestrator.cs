using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed class InMemoryOrchestrator(
    IWorkTracker tracker,
    IWorkExecutor executor,
    OrchestratorOptions options,
    IClock clock,
    ILogger<InMemoryOrchestrator> logger,
    IOperatorTelemetry? telemetry = null) : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, ActiveRun> activeRuns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetryRun> retryRuns = new(StringComparer.Ordinal);
    private readonly HashSet<string> terminalDispatchSuppression = new(StringComparer.Ordinal);
    private readonly List<RunState> history = [];
    private readonly List<PendingHumanFeedbackMonitor> pendingHumanFeedbackMonitors = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool startupReconciled;
    private bool stopping;

    public bool IsStartupReconciled { get { lock (gate) return startupReconciled; } }

    public void RestoreTerminalState(string workItemId, string state, int attempt, string? reason)
    {
        if (state is not (RunStates.Completed or RunStates.Blocked or RunStates.Cancelled))
            throw new ArgumentException("Only terminal run states can be restored.", nameof(state));
        lock (gate)
        {
            history.RemoveAll(x => x.WorkItemId == workItemId);
            history.Add(new RunState(workItemId, state, attempt, clock.UtcNow, null, reason));
            retryRuns.Remove(workItemId);
            terminalDispatchSuppression.Add(workItemId);
        }
        telemetry?.Record("run.restored", OperatorEventSeverities.Information, workItemId, "recovery", message: reason, metadata: new Dictionary<string, string> { ["state"] = state, ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    public void RestoreRetry(WorkItem item, int nextAttempt, DateTimeOffset? retryNotBeforeUtc, string? reason)
    {
        if (nextAttempt < 1) throw new ArgumentOutOfRangeException(nameof(nextAttempt));
        lock (gate)
        {
            if (terminalDispatchSuppression.Contains(item.Id)) return;
            var retryAt = retryNotBeforeUtc ?? clock.UtcNow;
            var state = new RunState(item.Id, RunStates.RetryQueued, Math.Max(0, nextAttempt - 1), clock.UtcNow, retryAt, reason);
            history.RemoveAll(x => x.WorkItemId == item.Id);
            history.Add(state);
            retryRuns[item.Id] = new RetryRun(item, nextAttempt, state);
        }
        telemetry?.Record("run.retry.restored", OperatorEventSeverities.Warning, item.Id, "retry", message: reason, metadata: new Dictionary<string, string> { ["nextAttempt"] = nextAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    public void RestoreIneligible(string workItemId, int attempt, string? reason)
    {
        lock (gate)
        {
            history.RemoveAll(x => x.WorkItemId == workItemId);
            history.Add(new RunState(workItemId, RunStates.Ineligible, attempt, clock.UtcNow, null, reason));
            retryRuns.Remove(workItemId);
        }
    }

    public void RestoreAwaitingHumanFeedback(WorkItem item, int attempt, Func<CancellationToken, Task<RunOutcome>> monitor)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(monitor);
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));

        ActiveRun run;
        lock (gate)
        {
            if (stopping || terminalDispatchSuppression.Contains(item.Id) || activeRuns.ContainsKey(item.Id)) return;
            history.RemoveAll(x => x.WorkItemId == item.Id);
            retryRuns.Remove(item.Id);
            run = new ActiveRun(item, attempt, new CancellationTokenSource(), Task.CompletedTask,
                new RunState(item.Id, RunStates.Working, attempt, clock.UtcNow));
            activeRuns[item.Id] = run;
            pendingHumanFeedbackMonitors.Add(new PendingHumanFeedbackMonitor(run, monitor));
        }
        telemetry?.Record("run.restored", OperatorEventSeverities.Information, item.Id, "human-feedback",
            message: "Restored published draft awaiting human feedback.",
            metadata: new Dictionary<string, string>
            {
                ["state"] = RunStates.Working,
                ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    public void CompleteStartupReconciliation()
    {
        PendingHumanFeedbackMonitor[] monitors;
        lock (gate)
        {
            startupReconciled = true;
            monitors = [.. pendingHumanFeedbackMonitors];
            pendingHumanFeedbackMonitors.Clear();
        }
        foreach (var monitor in monitors) StartRestoredHumanFeedbackMonitor(monitor);
    }

    public IReadOnlyList<RunState> Snapshot()
    {
        lock (gate) return [.. history, .. activeRuns.Values.Select(x => x.State), .. retryRuns.Values.Select(x => x.State)];
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (IsStopping() || !IsStartupReconciled) return;
        await DispatchDueRetriesAsync(cancellationToken);
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
            if (stopping || terminalDispatchSuppression.Contains(item.Id) || activeRuns.ContainsKey(item.Id) || retryRuns.ContainsKey(item.Id) || activeRuns.Count >= options.Agent.MaxConcurrentAgents) return false;
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
        telemetry?.Record("run.started", OperatorEventSeverities.Information, item.Id, "execution", message: "Work item execution started.", metadata: new Dictionary<string, string> { ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) });
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

    private async Task MonitorRestoredHumanFeedbackAsync(ActiveRun run, Func<CancellationToken, Task<RunOutcome>> monitor)
    {
        try
        {
            var outcome = await monitor(run.Cancellation.Token);
            Complete(run.WorkItem.Id, outcome.Succeeded ? RunStates.Completed : RunStates.Blocked, run.Attempt, outcome.FailureReason);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            Complete(run.WorkItem.Id, RunStates.Cancelled, run.Attempt, null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Restored human-feedback monitoring failed for work item {WorkItemId}", run.WorkItem.Id);
            Complete(run.WorkItem.Id, RunStates.Blocked, run.Attempt, "human feedback monitoring failed");
        }
    }

    private void StartRestoredHumanFeedbackMonitor(PendingHumanFeedbackMonitor monitor)
    {
        lock (gate)
        {
            if (stopping || !activeRuns.TryGetValue(monitor.Run.WorkItem.Id, out var active) || active.Cancellation.IsCancellationRequested) return;
            var task = Task.Run(() => MonitorRestoredHumanFeedbackAsync(active, monitor.Monitor));
            activeRuns[active.WorkItem.Id] = active with { Task = task };
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
            _ = RetryWhenDueAsync(run.WorkItem.Id, delay);
        }
        telemetry?.Record("run.retry.queued", OperatorEventSeverities.Warning, run.WorkItem.Id, "retry", message: failureReason, metadata: new Dictionary<string, string> { ["nextAttempt"] = (run.Attempt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private async Task RetryWhenDueAsync(string workItemId, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, lifetimeCancellation.Token);
            await TryDispatchDueRetryAsync(workItemId, lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task DispatchDueRetriesAsync(CancellationToken cancellationToken)
    {
        string[] dueRetryIds;
        lock (gate)
        {
            dueRetryIds = retryRuns.Values
                .Where(x => !x.IsDispatching && x.State.RetryAtUtc <= clock.UtcNow)
                .Select(x => x.WorkItem.Id)
                .ToArray();
        }
        foreach (var workItemId in dueRetryIds)
            await TryDispatchDueRetryAsync(workItemId, cancellationToken);
    }

    private async Task TryDispatchDueRetryAsync(string workItemId, CancellationToken cancellationToken)
    {
        RetryRun retry;
        lock (gate)
        {
            if (stopping
                || !retryRuns.TryGetValue(workItemId, out var currentRetry)
                || currentRetry.IsDispatching
                || currentRetry.State.RetryAtUtc > clock.UtcNow) return;
            retry = currentRetry with { IsDispatching = true };
            retryRuns[workItemId] = retry;
        }

        WorkItem? refreshed;
        try { refreshed = await tracker.GetByIdAsync(workItemId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { RestoreRetry(workItemId); return; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Retry refresh failed for work item {WorkItemId}", workItemId);
            RestoreRetry(workItemId);
            return;
        }

        if (refreshed is null || !IsEligible(refreshed))
        {
            ReleaseRetry(workItemId, RunStates.Ineligible);
            return;
        }

        if (TryClaimRetry(workItemId, refreshed, retry.NextAttempt)) StartClaim(refreshed, retry.NextAttempt);
    }

    private bool TryClaimRetry(string workItemId, WorkItem refreshed, int attempt)
    {
        lock (gate)
        {
            if (stopping || !retryRuns.ContainsKey(workItemId)) return false;
            if (activeRuns.Count >= options.Agent.MaxConcurrentAgents)
            {
                var retry = retryRuns[workItemId];
                retryRuns[workItemId] = retry with { IsDispatching = false };
                return false;
            }
            retryRuns.Remove(workItemId);
            var cancellation = new CancellationTokenSource();
            activeRuns[workItemId] = new ActiveRun(refreshed, attempt, cancellation, Task.CompletedTask, new RunState(workItemId, RunStates.Claimed, attempt, clock.UtcNow));
            return true;
        }
    }

    private void RestoreRetry(string workItemId)
    {
        lock (gate)
        {
            if (retryRuns.TryGetValue(workItemId, out var retry)) retryRuns[workItemId] = retry with { IsDispatching = false };
        }
    }

    private void ReleaseRetry(string workItemId, string state)
    {
        lock (gate)
        {
            if (!retryRuns.Remove(workItemId, out var retry)) return;
            history.Add(new RunState(workItemId, state, retry.NextAttempt, clock.UtcNow));
        }
    }

    private void Complete(string workItemId, string state, int attempt, string? failureReason)
    {
        lock (gate)
        {
            activeRuns.Remove(workItemId);
            history.Add(new RunState(workItemId, state, attempt, clock.UtcNow, null, failureReason));
            if (state is RunStates.Completed or RunStates.Blocked or RunStates.Cancelled)
                terminalDispatchSuppression.Add(workItemId);
        }
        telemetry?.Record("run.completed", state == RunStates.Completed ? OperatorEventSeverities.Information : OperatorEventSeverities.Warning, workItemId, "terminal", message: failureReason, metadata: new Dictionary<string, string> { ["state"] = state, ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) });
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
    private sealed record RetryRun(WorkItem WorkItem, int NextAttempt, RunState State, bool IsDispatching = false);
    private sealed record PendingHumanFeedbackMonitor(ActiveRun Run, Func<CancellationToken, Task<RunOutcome>> Monitor);
}
