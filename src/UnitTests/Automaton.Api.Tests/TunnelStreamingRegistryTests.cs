using Automaton.Api.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public class TunnelStreamingRegistryTests
{
    private static async Task<(TunnelRequestRegistry Registry, string Id, TunnelResponse Head)> OpenStreamAsync()
    {
        var registry = new TunnelRequestRegistry();
        var (id, task) = registry.Register(TimeSpan.FromSeconds(30), CancellationToken.None);
        registry.TryComplete(new TunnelResponse { RequestId = id, StatusCode = 200, Streaming = true });
        return (registry, id, await task);
    }

    [Test]
    public async Task Chunks_ArriveInOrder_AndTheFinalChunkEndsTheStream()
    {
        var (registry, id, head) = await OpenStreamAsync();

        Assert.That(registry.TryWriteChunk(id, [1], final: false, failed: false), Is.True);
        Assert.That(registry.TryWriteChunk(id, [2], final: true, failed: false), Is.True);

        var received = new List<byte[]>();
        await foreach (var chunk in head.BodyStream!.Reader.ReadAllAsync()) received.Add(chunk);
        Assert.That(received.Select(chunk => chunk[0]), Is.EqualTo(new byte[] { 1, 2 }));
    }

    [Test]
    public async Task ReaderThatFallsBehind_IsCutOffInsteadOfGrowingMemory()
    {
        var (registry, id, head) = await OpenStreamAsync();

        for (var i = 0; i < TunnelRequestRegistry.StreamCapacity; i++)
            Assert.That(registry.TryWriteChunk(id, [1], false, false), Is.True);
        Assert.That(registry.TryWriteChunk(id, [1], false, false), Is.False);

        Assert.That(registry.TryWriteChunk(id, [1], false, false), Is.False, "the stream is gone");
        Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in head.BodyStream!.Reader.ReadAllAsync()) { }
        });
    }

    [Test]
    public async Task FailedFinalChunk_SurfacesAsAnErrorToTheReader()
    {
        var (registry, id, head) = await OpenStreamAsync();

        registry.TryWriteChunk(id, [], final: true, failed: true);

        Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in head.BodyStream!.Reader.ReadAllAsync()) { }
        });
    }

    [Test]
    public void Chunk_ForUnknownRequest_IsRejected()
        => Assert.That(new TunnelRequestRegistry().TryWriteChunk("nope", [1], false, false), Is.False);

    [Test]
    public void BufferedResponse_HasNoBodyStream()
    {
        var registry = new TunnelRequestRegistry();
        var (id, task) = registry.Register(TimeSpan.FromSeconds(30), CancellationToken.None);
        registry.TryComplete(new TunnelResponse { RequestId = id, StatusCode = 200 });
        Assert.That(task.Result.BodyStream, Is.Null);
    }

    [Test]
    public void Limiter_CapsLongLivedConnectionsPerApp_AndFreesSlotsOnDispose()
    {
        var limiter = new TunnelLongLivedLimiter();
        var app = Guid.NewGuid();

        var first = limiter.TryAcquire(app, 2);
        using var second = limiter.TryAcquire(app, 2);
        Assert.That(limiter.TryAcquire(app, 2), Is.Null);
        Assert.That(limiter.TryAcquire(Guid.NewGuid(), 2), Is.Not.Null);

        first!.Dispose();
        first.Dispose();
        Assert.That(limiter.Count(app), Is.EqualTo(1));
        Assert.That(limiter.TryAcquire(app, 2), Is.Not.Null);
    }

    [Test]
    public void WebSocketFrames_AreQueuedPerRequest_AndOverflowClosesTheQueue()
    {
        var registry = new TunnelRequestRegistry();
        var reader = registry.RegisterWebSocket("ws");

        Assert.That(registry.TryWriteWebSocketFrame(new TunnelWebSocketFrame { RequestId = "ws", Data = [1] }), Is.True);
        Assert.That(registry.TryWriteWebSocketFrame(new TunnelWebSocketFrame { RequestId = "other" }), Is.False);
        Assert.That(reader.TryRead(out var frame), Is.True);
        Assert.That(frame!.Data, Is.EqualTo(new byte[] { 1 }));

        for (var i = 0; i < TunnelRequestRegistry.WebSocketCapacity; i++)
            registry.TryWriteWebSocketFrame(new TunnelWebSocketFrame { RequestId = "ws" });
        Assert.That(registry.TryWriteWebSocketFrame(new TunnelWebSocketFrame { RequestId = "ws" }), Is.False);
    }
}
