using System.Net;
using System.Net.Http.Headers;
using Automaton.Core.Models;
using Automaton.Runner.Console;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal static class TunnelForwarderBinaryResponseScenario
{
    public static async Task<TunnelResponse> RunAsync()
    {
        var handler = new StubHandler(request =>
        {
            Assert.That(
                request.RequestUri!.PathAndQuery,
                Is.EqualTo("/apps/11111111-1111-1111-1111-111111111111/targets/22222222-2222-2222-2222-222222222222/api/file?download=true"));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0, 255, 12])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            response.Headers.Connection.Add("close");
            return response;
        });
        var service = new TunnelForwarderService(
            Options.Create(new TunnelForwarderConfig { MaxBodyBytes = 16 }),
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            handler);
        var request = new TunnelRequest
        {
            RequestId = "request",
            Destination = TunnelDestinations.RuntimeGateway,
            RuntimeAppId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeDeploymentTargetId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            RuntimePath = "api/file?download=true"
        };

        return await service.ForwardAsync(request);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
