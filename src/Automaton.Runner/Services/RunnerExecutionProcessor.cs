using System.Collections.Concurrent;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal interface IRunnerExecutionExecutor
{
    Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token);
}

internal sealed class RunnerExecutionProcessor(RunnerExecutionJournal journal, IRunnerExecutionExecutor executor)
{
    private readonly ConcurrentDictionary<Guid, RunnerExecutionResult> completed = new();
    public async Task<RunnerExecutionResult> CancelAsync(Guid id, CancellationToken token)
    {
        using var ownership = await journal.LockAsync(id, token);
        var saved = await journal.ReadAsync(id, token);
        if (saved is not null) return saved;
        if (completed.TryGetValue(id, out var completedResult))
        {
            await journal.SaveAsync(id, completedResult, CancellationToken.None);
            completed.TryRemove(id, out _);
            return completedResult;
        }
        if (!journal.WasAccepted(id) && !journal.WasCancelled(id)) journal.CancelBeforeExecution(id);
        var result = journal.WasAccepted(id)
            ? RunnerExecutionResult.Interrupted("Cancellation followed interrupted execution; side effects are uncertain.")
            : CancelledBeforeExecution();
        await journal.SaveAsync(id, result, CancellationToken.None);
        return result;
    }
    public void Forget(Guid id) => journal.Forget(id);
    public async Task<RunnerExecutionResult> ProcessAsync(
        Guid id, RunnerExecutionRequest request, CancellationToken token,
        Func<RunnerExecutionRequest, CancellationToken, Task<RunnerExecutionResult>>? executeOverride = null)
    {
        using var ownership = await journal.LockAsync(id, token);
        var saved = await journal.ReadAsync(id, token);
        if (saved is not null) return saved;
        if (!completed.TryGetValue(id, out var result))
        {
            if (journal.WasCancelled(id)) result = CancelledBeforeExecution();
            else result = journal.WasAccepted(id)
                ? RunnerExecutionResult.Interrupted("Runner stopped after accepting work; execution outcome is uncertain. Work was not repeated.")
                : await ExecuteOnceAsync(id, request, token, executeOverride);
            completed[id] = result;
        }
        // A persistence error must never become an execution failure or cause work to run again.
        await journal.SaveAsync(id, result, CancellationToken.None);
        completed.TryRemove(id, out _);
        return result;
    }
    private static RunnerExecutionResult CancelledBeforeExecution() => new()
    {
        Outcome = "Cancelled", Error = "Cancelled before execution.",
        Started = DateTime.UtcNow, Finished = DateTime.UtcNow
    };
    private async Task<RunnerExecutionResult> ExecuteOnceAsync(Guid id, RunnerExecutionRequest request,
        CancellationToken token, Func<RunnerExecutionRequest, CancellationToken, Task<RunnerExecutionResult>>? executeOverride)
    {
        journal.Accept(id);
        try { return executeOverride is null
            ? await executor.ExecuteAsync(request, token)
            : await executeOverride(request, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new() { Outcome = "Cancelled", Error = "Cancellation requested; side effects may have occurred.",
                Started = DateTime.UtcNow, Finished = DateTime.UtcNow };
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "Runner execution {OperationId} failed.", id);
            return new() { Error = exception.Message, Logs = [$"[Error] {exception.Message}"], Started = DateTime.UtcNow, Finished = DateTime.UtcNow };
        }
    }
}
