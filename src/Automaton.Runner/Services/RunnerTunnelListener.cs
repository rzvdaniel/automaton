using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Connections;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// A Kestrel "transport" whose connections are not accepted from a socket: the Runner dials the API and hands each
/// open tunnel connection in. Kestrel then serves HTTP/2 on it, so the API's requests arrive as ordinary HTTP/2 streams.
/// </summary>
internal sealed class RunnerTunnelListener : IConnectionListenerFactory, IConnectionListener
{
    private readonly Channel<ConnectionContext> accepted = Channel.CreateUnbounded<ConnectionContext>();

    public EndPoint EndPoint { get; } = new TunnelEndPoint();

    /// <summary>Hands an open tunnel connection to Kestrel.</summary>
    public void Enqueue(TunnelConnectionStream stream)
    {
        if (!accepted.Writer.TryWrite(new TunnelConnectionContext(stream)))
            stream.Dispose();
    }

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IConnectionListener>(this);

    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        try { return await accepted.Reader.ReadAsync(cancellationToken); }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException) { return null; }
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        accepted.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => UnbindAsync();

    internal sealed class TunnelEndPoint : EndPoint
    {
        public override string ToString() => "runner-tunnel";
    }

    private sealed class TunnelConnectionContext : DefaultConnectionContext
    {
        private readonly TunnelConnectionStream stream;

        public TunnelConnectionContext(TunnelConnectionStream stream) : base(Guid.NewGuid().ToString("N"))
        {
            this.stream = stream;
            Transport = new DuplexPipe(PipeReader.Create(stream), PipeWriter.Create(stream));
            var closedSource = new CancellationTokenSource();
            ConnectionClosed = closedSource.Token;
            _ = stream.Closed.ContinueWith(_ => closedSource.Cancel(), TaskScheduler.Default);
        }

        public override void Abort(ConnectionAbortedException abortReason) => stream.Dispose();

        public override void Abort() => stream.Dispose();

        public override async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;
        public PipeWriter Output { get; } = output;
    }
}
