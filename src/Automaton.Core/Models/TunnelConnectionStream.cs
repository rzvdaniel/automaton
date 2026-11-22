namespace Automaton.Core.Models;

/// <summary>
/// The byte stream of one tunnel connection (a WebSocket both sides speak HTTP/2 over). It reports when the connection
/// ends, whether the peer closed it, a read or write failed, or HTTP/2 disposed it, so its owner can drop it.
/// </summary>
public sealed class TunnelConnectionStream(Stream inner) : Stream
{
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the connection is over.</summary>
    public Task Closed => closed.Task;

    /// <summary>Why the connection ended, for logs.</summary>
    public string? CloseReason { get; private set; }

    private void Close(string reason)
    {
        if (closed.TrySetResult()) CloseReason = reason;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read == 0 && buffer.Length > 0) Close("the peer closed the connection");
            return read;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Close(ex.GetType().Name + ": " + ex.Message); throw; }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try { await inner.WriteAsync(buffer, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Close(ex.GetType().Name + ": " + ex.Message); throw; }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Close("disposed");
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        Close("disposed");
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
