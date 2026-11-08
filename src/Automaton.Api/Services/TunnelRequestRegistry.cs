using System.Collections.Concurrent;
using System.Threading.Channels;
using Automaton.Core.Models;

namespace Automaton.Api.Services;

public class TunnelRequestRegistry
{
    /// <summary>Chunks buffered per streaming response. The Runner may have fewer chunks in flight (its window), so this never overflows.</summary>
    public const int StreamCapacity = 64;
    public const int WebSocketCapacity = 256;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<TunnelResponse>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<byte[]>> _streams = new();
    private readonly ConcurrentDictionary<string, Channel<TunnelWebSocketFrame>> _sockets = new();

    public (string requestId, Task<TunnelResponse> responseTask) Register(TimeSpan timeout, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<TunnelResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[requestId] = tcs;

        ct.Register(() => TryFail(requestId, "Request cancelled"));
        _ = Task.Delay(timeout, ct).ContinueWith(
            _ => TryFail(requestId, "Tunnel request timed out"),
            ct,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return (requestId, tcs.Task);
    }

    public bool TryComplete(TunnelResponse response)
    {
        if (!_pending.TryRemove(response.RequestId, out var tcs))
            return false;

        if (response.Streaming)
        {
            // Created before the response is released so the first chunk (processed right after this head) finds it.
            var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(StreamCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
            _streams[response.RequestId] = channel;
            response.BodyStream = new TunnelBodyStream(channel.Reader, () => Task.CompletedTask, () => Task.CompletedTask);
        }

        tcs.TrySetResult(response);
        return true;
    }

    /// <summary>
    /// Queues one body chunk. Returns false when the stream is unknown, finished or its reader fell behind;
    /// the caller then tells the Runner to stop sending.
    /// </summary>
    public bool TryWriteChunk(string requestId, byte[] data, bool final, bool failed)
    {
        if (!_streams.TryGetValue(requestId, out var channel))
            return false;

        if (data.Length > 0 && !channel.Writer.TryWrite(data))
        {
            EndStream(requestId, new IOException("The browser reads the response too slowly."));
            return false;
        }

        if (final)
            EndStream(requestId, failed ? new IOException("The Runner stopped the response.") : null);
        return true;
    }

    /// <summary>Ends a stream early (browser went away or the limit was hit).</summary>
    public void EndStream(string requestId, Exception? error = null)
    {
        if (_streams.TryRemove(requestId, out var channel))
            channel.Writer.TryComplete(error);
    }

    public ChannelReader<TunnelWebSocketFrame> RegisterWebSocket(string requestId)
    {
        var channel = Channel.CreateBounded<TunnelWebSocketFrame>(new BoundedChannelOptions(WebSocketCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _sockets[requestId] = channel;
        return channel.Reader;
    }

    public bool TryWriteWebSocketFrame(TunnelWebSocketFrame frame)
    {
        if (!_sockets.TryGetValue(frame.RequestId, out var channel))
            return false;
        if (!channel.Writer.TryWrite(frame))
        {
            RemoveWebSocket(frame.RequestId, new IOException("The browser reads WebSocket messages too slowly."));
            return false;
        }
        return true;
    }

    public void RemoveWebSocket(string requestId, Exception? error = null)
    {
        if (_sockets.TryRemove(requestId, out var channel))
            channel.Writer.TryComplete(error);
    }

    private void TryFail(string requestId, string reason)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetException(new TimeoutException(reason));
    }
}
