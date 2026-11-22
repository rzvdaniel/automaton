using NUnit.Framework;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;
using Automaton.Api.Config;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Runner.Console;
using Automaton.Runner.Console.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Automaton.Runner.Tests;

/// <summary>
/// The whole app tunnel in one process: browser, API proxy (YARP), tunnel connection (HTTP/2 over an in-memory duplex
/// stream, as it runs over a WebSocket), Runner (Kestrel + YARP) and a fake runtime gateway.
/// </summary>
[TestFixture]
public sealed class RunnerTunnelEndToEndTests
{
    private readonly Guid runnerId = Guid.NewGuid();
    private readonly Guid appId = Guid.NewGuid();
    private readonly Guid targetId = Guid.NewGuid();
    private WebApplication gateway = null!;
    private WebApplication api = null!;
    private RunnerTunnel runnerTunnel = null!;
    private RunnerTunnelRegistry registry = null!;
    private HttpClient browser = null!;
    private TunnelConnectionStream apiEnd = null!;

    [SetUp]
    public async Task SetUp()
    {
        gateway = await StartGatewayAsync();
        var gatewayUrl = gateway.Urls.First();
        runnerTunnel = new RunnerTunnel(null!, Options.Create(new TunnelForwarderConfig { RuntimeGatewayUrl = gatewayUrl }));
        registry = new RunnerTunnelRegistry();

        var (apiStream, runnerStream) = DuplexPair.Create();
        apiEnd = new TunnelConnectionStream(apiStream);
        registry.Add(runnerId, apiEnd, out _);
        await runnerTunnel.AcceptConnectionAsync(new TunnelConnectionStream(runnerStream), CancellationToken.None);

        api = await StartApiAsync();
        browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(api.Urls.First()), Timeout = TimeSpan.FromMinutes(2) };
    }

    [TearDown]
    public async Task TearDown()
    {
        browser.Dispose();
        await api.DisposeAsync();
        await runnerTunnel.DisposeAsync();
        await gateway.DisposeAsync();
    }

    private string AppPath(string rest) => $"/runtime-tunnel/apps/{appId:D}/targets/{targetId:D}/{rest}";

    [Test]
    public async Task Get_ReachesTheGatewayAtTheAppPathWithoutTheTunnelHeadersTheBrowserSent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppPath("hello?x=1"));
        request.Headers.TryAddWithoutValidation(TunnelHeaders.Destination, TunnelDestinations.FastApi);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example");

        using var response = await browser.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Does.StartWith($"/apps/{appId:D}/targets/{targetId:D}/hello?x=1|"));
            Assert.That(body, Does.Contain($"fwd={api.Urls.First().Replace("http://", "")}"));
            Assert.That(body, Does.Contain("tunnelheaders=0"));
        });
    }

    [Test]
    public async Task TwoGetsInARowUseTheSameConnection()
    {
        using var first = await browser.GetAsync(AppPath("hello"));
        using var second = await browser.GetAsync(AppPath("hello"));
                Assert.That((first.StatusCode, second.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
    }

    [Test]
    public async Task Post_StreamsABodyLargerThanTheOldMessageLimit()
    {
        var payload = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(payload);

        using var response = await browser.PostAsync(AppPath("echo"), new ByteArrayContent(payload));
        var echoed = await response.Content.ReadAsByteArrayAsync();

        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task RedirectsAndErrorStatusesPassThroughUnchanged()
    {
        using var redirect = await browser.GetAsync(AppPath("redirect"));
        using var missing = await browser.GetAsync(AppPath("missing"));

        Assert.Multiple(() =>
        {
            Assert.That(redirect.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            Assert.That(redirect.Headers.Location?.ToString(), Is.EqualTo("/elsewhere"));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task LargeDownload_StreamsWithFlatMemoryWhileOtherRequestsStayResponsive()
    {
        const long size = 400L * 1024 * 1024;
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        long peak = before;

        using var download = await browser.GetAsync(AppPath($"big?bytes={size}"), HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await download.Content.ReadAsStreamAsync();
        var buffer = new byte[256 * 1024];
        long total = 0;
        var slowest = TimeSpan.Zero;
        var nextProbe = 32L * 1024 * 1024;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            total += read;
            if (total < nextProbe) continue;
            nextProbe += 64L * 1024 * 1024;
            peak = Math.Max(peak, GC.GetTotalMemory(false));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using var other = await browser.GetAsync(AppPath("hello"));
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            slowest = watch.Elapsed > slowest ? watch.Elapsed : slowest;
        }

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(size));
            Assert.That(peak - before, Is.LessThan(150L * 1024 * 1024), "memory grew with the download");
            Assert.That(slowest, Is.LessThan(TimeSpan.FromSeconds(2)), "another request waited behind the download");
        });
    }

    [Test]
    public async Task WebSocket_UpgradesThroughTheTunnelAndEchoes()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(api.Urls.First().Replace("http", "ws") + AppPath("ws")), CancellationToken.None);
        await socket.SendAsync("ping"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[64];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.That(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count), Is.EqualTo("echo:ping"));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    [Test]
    public async Task ABrowserThatLeavesMidDownloadEndsTheProxiedRequestAndLeavesTheTunnelUsable()
    {
        using var download = await browser.GetAsync(AppPath("big?bytes=2000000000"), HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await download.Content.ReadAsStreamAsync();
        Assert.That(await stream.ReadAsync(new byte[1024]), Is.GreaterThan(0));
        await stream.DisposeAsync();
        download.Dispose();

        // The proxy must stop at once (it cancels the HTTP/2 stream, which cancels the Runner's read of the gateway)
        // and not wait for the 2 GB body; the tunnel connection stays usable for other visitors.
        await WaitForAsync(() => Diag.Returned > 0);
        using var after = await browser.GetAsync(AppPath("hello"));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task WhenTheTunnelConnectionEnds_TheRunnerIsNoLongerConnected()
    {
        Assert.That(registry.IsConnected(runnerId), Is.True);
        await apiEnd.DisposeAsync();
        Assert.That(registry.IsConnected(runnerId), Is.False);
        Assert.That(registry.TryAcquire(runnerId), Is.Null);
    }

    internal static class Diag { public static int Returned; }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.That(condition(), Is.True, "condition not met in time");
    }

    private static async Task<WebApplication> StartGatewayAsync()
    {
        Diag.Returned = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/apps/{app}/targets/{target}/{**rest}", async (HttpContext context, string rest) =>
        {
            switch (rest)
            {
                case "hello":
                    await context.Response.WriteAsync(
                        $"{context.Request.Path}{context.Request.QueryString}|fwd={context.Request.Headers["X-Forwarded-Host"]}" +
                        $"|tunnelheaders={TunnelHeaders.All.Count(h => context.Request.Headers.ContainsKey(h))}");
                    break;
                case "echo":
                    // Read it all first: an HTTP/1.1 browser sends the whole body before it reads, so echoing as it arrives would deadlock.
                    using (var received = new MemoryStream())
                    {
                        await context.Request.Body.CopyToAsync(received);
                        await context.Response.Body.WriteAsync(received.ToArray());
                    }
                    break;
                case "redirect":
                    context.Response.Redirect("/elsewhere");
                    break;
                case "big":
                    var remaining = long.Parse(context.Request.Query["bytes"]!);
                    var chunk = new byte[64 * 1024];
                    // Kestrel ignores writes after an abort instead of throwing, so the loop must look at the token.
                    while (remaining > 0 && !context.RequestAborted.IsCancellationRequested)
                    {
                        var count = (int)Math.Min(chunk.Length, remaining);
                        try { await context.Response.Body.WriteAsync(chunk.AsMemory(0, count), context.RequestAborted); }
                        catch (OperationCanceledException) { break; }
                        remaining -= count;
                    }
                    break;
                case "ws" when context.WebSockets.IsWebSocketRequest:
                    using (var socket = await context.WebSockets.AcceptWebSocketAsync())
                    {
                        var buffer = new byte[256];
                        var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        var reply = System.Text.Encoding.UTF8.GetBytes("echo:" + System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count));
                        await socket.SendAsync(reply, WebSocketMessageType.Text, true, CancellationToken.None);
                        var close = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        if (close.MessageType == WebSocketMessageType.Close)
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    }
                    break;
                default:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    break;
            }
        });
        await app.StartAsync();
        return app;
    }

    private async Task<WebApplication> StartApiAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpForwarder();
        var app = builder.Build();
        app.UseWebSockets();
        var sender = new RuntimeTunnelRequestSender(
            app.Services.GetRequiredService<IHttpForwarder>(), registry, new TunnelLongLivedLimiter(),
            Options.Create(new RuntimeTunnelConfig()), NullLogger<RuntimeTunnelRequestSender>.Instance);
        app.Map("/runtime-tunnel/apps/{appId:guid}/targets/{targetId:guid}/{**path}",
            async (HttpContext context, Guid appId, Guid targetId, string? path) =>
            {
                await sender.ProxyAsync(context, appId, targetId, new Automaton.Api.Entities.Runner { Id = runnerId }, path ?? string.Empty, staticSite: false);
                if (path == "big") Interlocked.Increment(ref Diag.Returned);
            });
        await app.StartAsync();
        return app;
    }

    /// <summary>Two connected in-memory byte streams, like the two ends of the tunnel WebSocket.</summary>
    private sealed class DuplexPair
    {
        public static (Stream First, Stream Second) Create()
        {
            var firstToSecond = new Pipe();
            var secondToFirst = new Pipe();
            return (new PipeDuplexStream(secondToFirst.Reader, firstToSecond.Writer),
                    new PipeDuplexStream(firstToSecond.Reader, secondToFirst.Writer));
        }
    }

    private sealed class PipeDuplexStream(PipeReader reader, PipeWriter writer) : Stream
    {
        private readonly Stream input = reader.AsStream();
        private readonly Stream output = writer.AsStream();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await output.WriteAsync(buffer, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => input.ReadAsync(buffer, offset, count, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { reader.Complete(); writer.Complete(); }
            base.Dispose(disposing);
        }
    }
}
