using System.Collections.Concurrent;
using Automaton.Core.Models;

namespace Automaton.Api.Services;

/// <summary>
/// The tunnel connections Runners opened to this API instance. A Runner dials out (WebSocket) and both sides then speak
/// HTTP/2 over that socket with the roles swapped: the API is the HTTP client, the Runner serves. Each connection is
/// one HTTP/2 connection with its own flow control, so a Runner keeps a few and a loss on one does not stall the rest.
/// </summary>
public sealed class RunnerTunnelRegistry
{
    // Bandwidth-delay product at 1 Gbit/s and 100 ms is about 12 MB; one stream may use that much window.
    internal const int StreamWindowBytes = 16 * 1024 * 1024;

    private readonly ConcurrentDictionary<Guid, List<Connection>> connections = new();

    public bool IsConnected(Guid runnerId) => connections.TryGetValue(runnerId, out var list) && Count(list) > 0;

    public int ConnectionCount(Guid runnerId) => connections.TryGetValue(runnerId, out var list) ? Count(list) : 0;

    /// <summary>Registers an accepted tunnel socket. Dispose the result when the socket ends.</summary>
    public IDisposable Add(Guid runnerId, TunnelConnectionStream stream, out Task closed)
    {
        var connection = new Connection(stream);
        var list = connections.GetOrAdd(runnerId, _ => []);
        lock (list) list.Add(connection);
        closed = stream.Closed;
        return new Registration(this, runnerId, connection);
    }

    /// <summary>The least busy connection of the Runner, or null when it has none. Dispose the lease when the request ends.</summary>
    public TunnelLease? TryAcquire(Guid runnerId)
    {
        if (!connections.TryGetValue(runnerId, out var list)) return null;
        lock (list)
        {
            var best = list.Where(connection => !connection.Stream.Closed.IsCompleted)
                .OrderBy(connection => connection.InFlight).FirstOrDefault();
            if (best is null) return null;
            Interlocked.Increment(ref best.InFlight);
            return new TunnelLease(best.Invoker, () => Interlocked.Decrement(ref best.InFlight));
        }
    }

    private static int Count(List<Connection> list)
    {
        lock (list) return list.Count(connection => !connection.Stream.Closed.IsCompleted);
    }

    private void Remove(Guid runnerId, Connection connection)
    {
        if (connections.TryGetValue(runnerId, out var list))
        {
            lock (list) list.Remove(connection);
        }
        connection.Dispose();
    }

    private sealed class Registration(RunnerTunnelRegistry owner, Guid runnerId, Connection connection) : IDisposable
    {
        public void Dispose() => owner.Remove(runnerId, connection);
    }

    private sealed class Connection : IDisposable
    {
        private int used;
        public int InFlight;
        public TunnelConnectionStream Stream { get; }
        public HttpMessageInvoker Invoker { get; }

        public Connection(TunnelConnectionStream stream)
        {
            Stream = stream;
            Invoker = new HttpMessageInvoker(new SocketsHttpHandler
            {
                // The only "network" this handler has is the already open tunnel socket, handed out once.
                ConnectCallback = (_, _) => Interlocked.Exchange(ref used, 1) == 0
                    ? ValueTask.FromResult<Stream>(stream)
                    : throw new IOException("The tunnel connection is closed."),
                InitialHttp2StreamWindowSize = StreamWindowBytes,
                EnableMultipleHttp2Connections = false,
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
            });
        }

        public void Dispose() => Invoker.Dispose();
    }
}

/// <summary>One request's hold on a tunnel connection; disposing it frees the connection for the next pick.</summary>
public sealed class TunnelLease(HttpMessageInvoker invoker, Action release) : IDisposable
{
    private int released;
    public HttpMessageInvoker Invoker { get; } = invoker;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref released, 1) == 0) release();
    }
}
