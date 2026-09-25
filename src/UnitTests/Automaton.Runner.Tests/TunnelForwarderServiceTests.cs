using Automaton.Core.Models;
using Automaton.Runner.Console;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class TunnelForwarderServiceTests
{
    [TestCase("", true)]
    [TestCase("api/health?value=test", true)]
    [TestCase("../secrets", false)]
    [TestCase("%2e%2e/secrets", false)]
    [TestCase("assets\\file.js", false)]
    [TestCase("https://example.invalid/", false)]
    public void TryBuildRuntimeGatewayPath_ConstrainsPathToAssignedTarget(string runtimePath, bool expected)
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new TunnelRequest
        {
            RuntimeAppId = appId,
            RuntimeDeploymentTargetId = targetId,
            RuntimePath = runtimePath
        };

        var accepted = TunnelForwarderService.TryBuildRuntimeGatewayPath(request, out var path);

        Assert.That(accepted, Is.EqualTo(expected));
        if (accepted)
            Assert.That(path, Does.StartWith($"/apps/{appId:D}/targets/{targetId:D}/"));
    }

    [TestCase("Content-Type", true)]
    [TestCase("Connection", false)]
    [TestCase("Transfer-Encoding", false)]
    [TestCase("Upgrade", false)]
    [TestCase("Keep-Alive", false)]
    public void IsForwardableResponseHeader_FiltersHopByHopHeaders(string header, bool expected)
    {
        Assert.That(TunnelForwarderService.IsForwardableResponseHeader(header), Is.EqualTo(expected));
    }

    [Test]
    public async Task ForwardAsync_PreservesBinaryResponseContentTypeAndQuery()
    {
        var response = await TunnelForwarderBinaryResponseScenario.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(response.BodyBytes, Is.EqualTo(new byte[] { 0, 255, 12 }));
            Assert.That(response.HeaderValues["Content-Type"], Does.Contain("application/octet-stream"));
            Assert.That(response.HeaderValues.ContainsKey("Connection"), Is.False);
        });
    }

    [Test]
    public async Task ForwardAsync_EnforcesResponseLimit()
    {
        var service = new TunnelForwarderService(
            Options.Create(new TunnelForwarderConfig { MaxBodyBytes = 2 }),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            }));
        var response = await service.ForwardAsync(new TunnelRequest
        {
            Destination = TunnelDestinations.RuntimeGateway,
            RuntimeAppId = Guid.NewGuid(),
            RuntimeDeploymentTargetId = Guid.NewGuid(),
            RuntimePath = string.Empty
        });

        Assert.That(response.StatusCode, Is.EqualTo(413));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
