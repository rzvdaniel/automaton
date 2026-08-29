using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerRuntimeGatewayServiceTests
{
    [Test]
    public async Task Gateway_ForwardsArbitraryRuntimePathsWithoutTechnologySpecificRouting()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var registry = new RunnerRuntimeRegistry();
        registry.Register(appId, targetId, 18_321);

        var factory = new CapturingHttpClientFactory();
        var gateway = new RunnerRuntimeGatewayService(
            registry,
            factory,
            NullLogger<RunnerRuntimeGatewayService>.Instance);
        var previousPort = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        var port = GetFreePort();
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", port.ToString());

        try
        {
            await gateway.StartAsync(CancellationToken.None);

            using var client = new HttpClient();
            using var response = await client.PostAsync(
                $"http://127.0.0.1:{port}/apps/{appId:D}/targets/{targetId:D}/orders/42?expand=items",
                new StringContent("payload"));

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(factory.Request?.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(factory.Request?.RequestUri?.PathAndQuery, Is.EqualTo("/orders/42?expand=items"));
            });
        }
        finally
        {
            await gateway.StopAsync(CancellationToken.None);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", previousPort);
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        public HttpRequestMessage? Request { get; private set; }

        public HttpClient CreateClient(string name) => new(new CapturingHandler(this));

        private sealed class CapturingHandler(CapturingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.Request = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("runtime response")
                });
            }
        }
    }
}
