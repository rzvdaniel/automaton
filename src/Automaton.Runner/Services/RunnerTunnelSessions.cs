using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Long-lived tunnel work on the Runner: streamed response bodies and WebSockets. Each runs on its own task so one
/// slow or busy app never blocks the SignalR receive loop, and each app has a cap on concurrent tunnel work.
/// </summary>
internal sealed class RunnerTunnelSessions(TunnelForwarderConfig config, ILogger logger)
{
    internal const int ChunkBytes = 32 * 1024;
    // API acknowledges every 8 chunks; the Runner never has more than this many unacknowledged.
    internal const int SendWindowChunks = 32;
    internal const int AckEveryChunks = 8;
    private static readonly TimeSpan AckTimeout = TimeSpan.FromMinutes(6);

    private readonly ConcurrentDictionary<string, StreamState> streams = new();
    private readonly ConcurrentDictionary<string, SocketSession> sockets = new();
    private readonly ConcurrentDictionary<Guid, int> activePerApp = new();

    /// <summary>Takes one of the app's slots, or null when the app already has its share in flight.</summary>
    public IDisposable? TryEnter(Guid? appId)
    {
        if (appId is not { } id) return NoLease.Instance;
        var count = activePerApp.AddOrUpdate(id, 1, (_, current) => current + 1);
        if (count > config.MaxConcurrentRequestsPerApp)
        {
            Release(id);
            return null;
        }
        return new Lease(this, id);
    }

    public void Acknowledge(string requestId)
    {
        if (streams.TryGetValue(requestId, out var state))
            state.Window.Release(AckEveryChunks);
    }

    public void Cancel(string requestId)
    {
        if (streams.TryGetValue(requestId, out var stream)) stream.Cancellation.Cancel();
        if (sockets.TryGetValue(requestId, out var socket)) socket.Cancellation.Cancel();
    }

    /// <summary>Registers a stream before its head is sent, so a cancel sent right after the head is not lost.</summary>
    public void PrepareStream(string requestId) => streams[requestId] = new StreamState();

    public async Task SendBodyAsync(HubConnection connection, string requestId, TunnelForwardResult result)
    {
        if (!streams.TryGetValue(requestId, out var state))
            streams[requestId] = state = new StreamState();
        using var owned = state;
        var failed = false;
        try
        {
            await using var body = await result.OpenBodyAsync(state.Cancellation.Token);
            var buffer = new byte[ChunkBytes];
            int read;
            while ((read = await body.ReadAsync(buffer, state.Cancellation.Token)) > 0)
            {
                // No acknowledgement for this long means the API stopped reading: give the slot back.
                if (!await state.Window.WaitAsync(AckTimeout, state.Cancellation.Token))
                    throw new TimeoutException("The API stopped acknowledging the stream.");
                await connection.SendAsync(
                    "TunnelResponseChunk", requestId, buffer.AsSpan(0, read).ToArray(), false, false,
                    state.Cancellation.Token);
            }
        }
        catch (Exception exception)
        {
            failed = !state.Cancellation.IsCancellationRequested;
            logger.Debug(exception, "Tunnel stream ended early: RequestId={RequestId}", requestId);
        }
        finally
        {
            streams.TryRemove(requestId, out _);
        }

        try { await connection.SendAsync("TunnelResponseChunk", requestId, Array.Empty<byte>(), true, failed); }
        catch (Exception exception) { logger.Debug(exception, "Could not end tunnel stream {RequestId}", requestId); }
    }

    public async Task RunWebSocketAsync(HubConnection connection, TunnelRequest request)
    {
        using var state = new SocketSession();
        sockets[request.RequestId] = state;
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        try
        {
            var opened = await OpenAsync(socket, request, state.Cancellation.Token);
            await connection.SendAsync("TunnelResponse", opened.Response);
            if (!opened.Success) return;

            var toApp = Task.Run(() => PumpToAppAsync(socket, state));
            var fromApp = TunnelWebSocketPump.ReadAsync(socket, request.RequestId,
                frame => connection.SendAsync("TunnelWebSocketFrame", frame, state.Cancellation.Token),
                state.Cancellation.Token);
            await Task.WhenAny(toApp, fromApp);
            state.Outbound.Writer.TryComplete();
            await Task.WhenAny(Task.WhenAll(toApp, fromApp), Task.Delay(TimeSpan.FromSeconds(5)));
            state.Cancellation.Cancel();
            try
            {
                await connection.SendAsync("TunnelWebSocketFrame", new TunnelWebSocketFrame
                {
                    RequestId = request.RequestId, Kind = TunnelWebSocketFrameKinds.Close, Data = []
                }, CancellationToken.None);
            }
            catch (Exception exception) { logger.Debug(exception, "Could not close tunnel WebSocket {RequestId}", request.RequestId); }
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "Tunnel WebSocket ended: RequestId={RequestId}", request.RequestId);
        }
        finally
        {
            sockets.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>API → app frames. Queued so the SignalR receive loop never waits on the app.</summary>
    public void QueueFrame(TunnelWebSocketFrame frame)
    {
        if (sockets.TryGetValue(frame.RequestId, out var state) && !state.Outbound.Writer.TryWrite(frame))
            state.Cancellation.Cancel();
    }

    private static async Task PumpToAppAsync(ClientWebSocket socket, SocketSession state)
    {
        try
        {
            await foreach (var frame in state.Outbound.Reader.ReadAllAsync(state.Cancellation.Token))
                if (!await TunnelWebSocketPump.WriteAsync(socket, frame, state.Cancellation.Token)) break;
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
        }
    }

    private async Task<(bool Success, TunnelResponse Response)> OpenAsync(
        ClientWebSocket socket, TunnelRequest request, CancellationToken cancellationToken)
    {
        if (!TunnelForwarderService.TryBuildRuntimeGatewayPath(request, out var gatewayPath))
            return (false, new TunnelResponse { RequestId = request.RequestId, StatusCode = 400, Error = "Runtime tunnel destination is invalid." });

        foreach (var (name, values) in request.HeaderValues)
        {
            if (IsHandshakeHeader(name)) continue;
            try { socket.Options.SetRequestHeader(name, string.Join(", ", values)); }
            catch (ArgumentException) { }
        }
        foreach (var subProtocol in request.WebSocketSubProtocols)
            socket.Options.AddSubProtocol(subProtocol);

        var uri = new Uri($"ws://127.0.0.1:{RunnerPlatformPorts.GetRuntimeGatewayPort()}{gatewayPath}");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await socket.ConnectAsync(uri, timeout.Token);
        }
        catch (Exception exception)
        {
            var status = socket.HttpStatusCode is var code && (int)code >= 400 ? (int)code : 502;
            return (false, new TunnelResponse
            {
                RequestId = request.RequestId, StatusCode = status, Error = exception.Message
            });
        }

        var response = new TunnelResponse
        {
            RequestId = request.RequestId, StatusCode = 101, UpstreamMs = stopwatch.ElapsedMilliseconds
        };
        if (socket.SubProtocol is { Length: > 0 } negotiated)
            response.HeaderValues["Sec-WebSocket-Protocol"] = [negotiated];
        return (true, response);
    }

    private static bool IsHandshakeHeader(string name)
        => name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase);

    private void Release(Guid appId)
        => activePerApp.AddOrUpdate(appId, 0, (_, current) => Math.Max(0, current - 1));

    private sealed class StreamState : IDisposable
    {
        public SemaphoreSlim Window { get; } = new(SendWindowChunks);
        public CancellationTokenSource Cancellation { get; } = new();
        public void Dispose() { Window.Dispose(); Cancellation.Dispose(); }
    }

    private sealed class SocketSession : IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Channel<TunnelWebSocketFrame> Outbound { get; } = Channel.CreateBounded<TunnelWebSocketFrame>(256);
        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class Lease(RunnerTunnelSessions owner, Guid appId) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) owner.Release(appId);
        }
    }

    private sealed class NoLease : IDisposable
    {
        public static readonly NoLease Instance = new();
        public void Dispose() { }
    }
}
