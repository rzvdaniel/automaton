using Automaton.Core.Models;
using Automaton.Runner.Console;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class TunnelStreamingTests
{
    private static TunnelRequest GatewayRequest() => new()
    {
        Method = "GET",
        Destination = TunnelDestinations.RuntimeGateway,
        RuntimeAppId = Guid.NewGuid(),
        RuntimeDeploymentTargetId = Guid.NewGuid(),
        RuntimePath = "events"
    };

    private static TunnelForwarderService Create(Func<HttpResponseMessage> response, int maxBodyBytes = 512 * 1024)
        => new(Options.Create(new TunnelForwarderConfig { MaxBodyBytes = maxBodyBytes }),
            null, new Handler(response));

    private static HttpResponseMessage EventStream() => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("data: 1\n\n")))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") }
        }
    };

    [Test]
    public async Task EventStream_StreamsWhenAllowed_AndKeepsTheBodyReadable()
    {
        using var result = await Create(EventStream).ForwardAsync(GatewayRequest(), allowStreaming: true);

        Assert.That(result.IsStreaming, Is.True);
        Assert.That(result.Response.Streaming, Is.True);
        Assert.That(result.Response.BodyBytes, Is.Null);
        using var reader = new StreamReader(await result.OpenBodyAsync(CancellationToken.None));
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("data: 1\n\n"));
    }

    [Test]
    public async Task EventStream_IsBufferedWhenStreamingIsNotAllowed()
    {
        var response = await Create(EventStream).ForwardAsync(GatewayRequest());

        Assert.That(response.Streaming, Is.False);
        Assert.That(Encoding.UTF8.GetString(response.BodyBytes!), Is.EqualTo("data: 1\n\n"));
    }

    [Test]
    public async Task SmallKnownLengthResponse_StaysBuffered()
    {
        using var result = await Create(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
        }).ForwardAsync(GatewayRequest(), allowStreaming: true);

        Assert.That(result.IsStreaming, Is.False);
        Assert.That(result.Response.BodyBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task ResponseLargerThanOneMessage_Streams()
    {
        using var result = await Create(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[2000])
        }, maxBodyBytes: 1000).ForwardAsync(GatewayRequest(), allowStreaming: true);

        Assert.That(result.IsStreaming, Is.True);
    }

    [Test]
    public async Task Sessions_LimitConcurrentWorkPerApp()
    {
        var sessions = new RunnerTunnelSessions(
            new TunnelForwarderConfig { MaxConcurrentRequestsPerApp = 2 }, Serilog.Log.Logger);
        var busyApp = Guid.NewGuid();

        using var first = sessions.TryEnter(busyApp);
        using var second = sessions.TryEnter(busyApp);
        var third = sessions.TryEnter(busyApp);
        using var otherApp = sessions.TryEnter(Guid.NewGuid());

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(third, Is.Null, "a busy app is refused instead of queuing");
        Assert.That(otherApp, Is.Not.Null, "other apps keep their own share");
        first!.Dispose();
        using var afterRelease = sessions.TryEnter(busyApp);
        Assert.That(afterRelease, Is.Not.Null);
        await Task.CompletedTask;
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }
}
