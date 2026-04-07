using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Automaton.Mcp.Tools;

public sealed class UiAutomationActionAckService : IUiAutomationActionAckService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _earlyResults = new(StringComparer.Ordinal);

    public async Task<string?> WaitForResultAsync(string toolCallId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolCallId))
            return null;

        if (_earlyResults.TryRemove(toolCallId, out var earlyResult))
            return earlyResult;

        var tcs = _pending.GetOrAdd(
            toolCallId,
            _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

        try
        {
            using var timeoutCts = timeout <= TimeSpan.Zero ? null : new CancellationTokenSource(timeout);
            using var linked = timeoutCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, linked.Token));
            if (completed == tcs.Task)
                return await tcs.Task;

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(toolCallId, out _);
        }
    }

    public void SubmitResult(string toolCallId, string resultJson)
    {
        if (string.IsNullOrWhiteSpace(toolCallId))
            return;

        if (_pending.TryGetValue(toolCallId, out var tcs))
        {
            tcs.TrySetResult(resultJson);
            return;
        }

        _earlyResults[toolCallId] = resultJson;
    }
}
