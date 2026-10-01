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
        var result = journal.WasAccepted(id)
            ? RunnerExecutionResult.Interrupted("Cancellation followed interrupted execution; side effects are uncertain.")
            : new RunnerExecutionResult { Outcome = "Cancelled", Error = "Cancelled before execution.",
                Started = DateTime.UtcNow, Finished = DateTime.UtcNow };
        await journal.SaveAsync(id, result, CancellationToken.None);
        return result;
    }
    public void Forget(Guid id) => journal.Forget(id);
    public async Task<RunnerExecutionResult> ProcessAsync(Guid id, RunnerExecutionRequest request, CancellationToken token)
    {
        using var ownership = await journal.LockAsync(id, token);
        var saved = await journal.ReadAsync(id, token);
        if (saved is not null) return saved;
        if (!completed.TryGetValue(id, out var result))
        {
            result = journal.WasAccepted(id)
                ? RunnerExecutionResult.Interrupted("Runner stopped after accepting work; execution outcome is uncertain. Work was not repeated.")
                : await ExecuteOnceAsync(id, request, token);
            completed[id] = result;
        }
        // A persistence error must never become an execution failure or cause work to run again.
        await journal.SaveAsync(id, result, CancellationToken.None);
        completed.TryRemove(id, out _);
        return result;
    }
    private async Task<RunnerExecutionResult> ExecuteOnceAsync(Guid id, RunnerExecutionRequest request, CancellationToken token)
    {
        journal.Accept(id);
        try { return await executor.ExecuteAsync(request, token); }
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
