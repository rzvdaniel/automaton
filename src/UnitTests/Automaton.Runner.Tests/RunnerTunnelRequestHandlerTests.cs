using Automaton.Core.Models;
using Automaton.Runner.Console;
using Automaton.Runner.Console.Services;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Yarp.ReverseProxy.Forwarder;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class RunnerTunnelRequestHandlerTests
{
    [TestCase("hello?x=1", true)]
    [TestCase("/a/b/c.css", true)]
    [TestCase("a%20b/c", true)]
    [TestCase("", true)]
    [TestCase("../etc/passwd", false)]
    [TestCase("a/%2e%2e/b", false)]
    [TestCase("a/%2E/b", false)]
    [TestCase("a%2fb/c", false)]
    [TestCase("a\\b", false)]
    [TestCase("http://evil.example/x", false)]
    public void TryReadSafeRuntimePath_KeepsTheRequestInsideTheApp(string path, bool allowed)
        => Assert.That(RunnerTunnelRequestHandler.TryReadSafeRuntimePath(path, out _, out _), Is.EqualTo(allowed));

    [Test]
    public async Task ResolveAsync_BuildsTheGatewayPathFromTheTrustedIdsOnly()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = CreateHandler();
        var context = Context("/assets/site.css", "?v=2", TunnelDestinations.RuntimeGateway, appId, targetId);

        var route = await handler.ResolveAsync(context, CancellationToken.None);

        Assert.That(route?.Uri.AbsoluteUri, Is.EqualTo($"http://127.0.0.1:8080/apps/{appId:D}/targets/{targetId:D}/assets/site.css?v=2"));
    }

    [Test]
    public async Task ResolveAsync_SendsFastApiCallsToTheLocalFastApiProcess()
    {
        var context = Context("/api/runtime-file", string.Empty, TunnelDestinations.FastApi, null, null);

        var route = await CreateHandler().ResolveAsync(context, CancellationToken.None);

        Assert.That(route?.Uri.AbsoluteUri, Is.EqualTo("http://localhost:8001/api/runtime-file"));
    }

    [TestCase("", true, true)]
    [TestCase("unknown-destination", true, true)]
    [TestCase(TunnelDestinations.RuntimeGateway, false, true)]
    [TestCase(TunnelDestinations.RuntimeGateway, true, false)]
    [TestCase(TunnelDestinations.StaticSite, true, true)]
    public async Task ResolveAsync_RefusesRequestsWithoutAValidDestination(string destination, bool withIds, bool traversal)
    {
        var path = traversal ? "/../x" : "/x";
        var context = Context(path, string.Empty, destination, withIds ? Guid.NewGuid() : null, withIds ? Guid.NewGuid() : null);

        var route = await CreateHandler().ResolveAsync(context, CancellationToken.None);

        // StaticSite has no publisher in this test, so it is refused as well.
        Assert.That(route, destination == TunnelDestinations.RuntimeGateway && withIds && !traversal ? Is.Not.Null : Is.Null);
    }

    [Test]
    public async Task HandleAsync_AnswersBusyWhileTheRunnerSwitchesApiInstance()
    {
        var handler = CreateHandler();
        handler.TryBeginRequest = () => false;
        var completed = 0;
        handler.CompleteRequest = () => completed++;
        var context = Context("/x", string.Empty, TunnelDestinations.RuntimeGateway, Guid.NewGuid(), Guid.NewGuid());
        context.Response.Body = new MemoryStream();

        await handler.HandleAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(completed, Is.Zero, "a refused request must not count as completed");
        });
    }

    private static RunnerTunnelRequestHandler CreateHandler()
        => new(null!, new TunnelForwarderConfig(), staticSitePublisher: null);

    private static DefaultHttpContext Context(string path, string query, string destination, Guid? appId, Guid? targetId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path.StartsWith("/") ? path : "/" + path;
        context.Request.QueryString = new QueryString(query);
        context.Request.Headers[TunnelHeaders.Destination] = destination;
        if (appId is { } app) context.Request.Headers[TunnelHeaders.AppId] = app.ToString("D");
        if (targetId is { } target) context.Request.Headers[TunnelHeaders.TargetId] = target.ToString("D");
        return context;
    }
}
