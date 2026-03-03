using System.Collections.Concurrent;
using Automaton.Core.Models;

namespace Automaton.Studio.Server.Services;

public class TunnelRequestRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TunnelResponse>> _pending = new();

    public (string requestId, Task<TunnelResponse> responseTask) Register(TimeSpan timeout, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<TunnelResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[requestId] = tcs;

        ct.Register(() => TryFail(requestId, "Request cancelled"));
        _ = Task.Delay(timeout).ContinueWith(_ => TryFail(requestId, "Tunnel request timed out"));

        return (requestId, tcs.Task);
    }

    public bool TryComplete(TunnelResponse response)
    {
        if (_pending.TryRemove(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
            return true;
        }
        return false;
    }

    private void TryFail(string requestId, string reason)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetException(new TimeoutException(reason));
    }
}
