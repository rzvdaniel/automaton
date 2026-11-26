using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerRuntimeGatewayProxyTests
{
    [TestCase("app-1.apps.example.test", "app-1.apps.example.test")]
    [TestCase("app-1.apps.example.test:8443", "app-1.apps.example.test:8443")]
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("a.example, b.example", null)]
    public void ApplyPublicHost_SendsTheVisitorsHostToTheApp(string? forwardedHost, string? expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://172.19.0.1:32788/");

        RunnerRuntimeGatewayProxy.ApplyPublicHost(request, forwardedHost);

        Assert.That(request.Headers.Host, Is.EqualTo(expected));
    }

    // #1778: a restarted app container gets a new host port; the first request after it must still be served.
    [Test]
    public async Task ProxyAsync_RefusedPort_AsksForTheCurrentPortAndRetriesOnce()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var deadPort = FreePort();
        using var live = new TcpListener(IPAddress.Loopback, 0);
        live.Start();
        var livePort = ((IPEndPoint)live.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            using var client = await live.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        });
        var registry = new RunnerRuntimeRegistry();
        registry.Register(appId, targetId, deadPort);
        var refresher = new FakeRefresher(livePort);
        var proxy = new RunnerRuntimeGatewayProxy(registry, new HttpClient(), refresher);
        var context = new DefaultHttpContext { Request = { Method = "GET" } };
        context.Response.Body = new MemoryStream();

        await proxy.ProxyAsync(context, appId, targetId, "/x");

        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(refresher.Calls, Is.EqualTo(new[] { deadPort }));
    }

    [Test]
    public async Task ProxyAsync_RefusedPortWithNoNewPort_Answers502()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var registry = new RunnerRuntimeRegistry();
        registry.Register(appId, targetId, FreePort());
        var refresher = new FakeRefresher(null);
        var proxy = new RunnerRuntimeGatewayProxy(registry, new HttpClient(), refresher);
        var context = new DefaultHttpContext { Request = { Method = "GET" } };
        context.Response.Body = new MemoryStream();

        await proxy.ProxyAsync(context, appId, targetId, "/x");

        Assert.That(context.Response.StatusCode, Is.EqualTo(502));
        Assert.That(refresher.Calls, Has.Count.EqualTo(1));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class FakeRefresher(int? port) : IRuntimePortRefresher
    {
        public List<int> Calls { get; } = [];

        public Task<int?> RefreshPortAsync(Guid appId, Guid deploymentTargetId, int failedPort, CancellationToken cancellationToken)
        {
            Calls.Add(failedPort);
            return Task.FromResult(port);
        }
    }
}
