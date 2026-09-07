using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerRuntimeGatewayServiceTests
{
    [Test]
    public async Task Gateway_ForwardsArbitraryMethodAndPreservesPathAndQuery()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler();
        await using var gateway = await StartGatewayAsync(appId, targetId, handler);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            new HttpMethod("PURGE"),
            gateway.GetUri($"/orders/42?expand=items%2Fsummary"))
        {
            Content = new StringContent("payload")
        };
        using var response = await client.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(handler.Request?.Method.Method, Is.EqualTo("PURGE"));
            Assert.That(handler.Request?.RequestUri?.PathAndQuery,
                Is.EqualTo("/orders/42?expand=items%2Fsummary"));
        });
    }

    [Test]
    public async Task GatewayUsesConfiguredRuntimeHostAddress()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler();
        await using var gateway = await StartGatewayAsync(appId, targetId, handler, "172.30.0.1");

        using var client = new HttpClient();
        using var response = await client.GetAsync(gateway.GetUri("/health"));

        Assert.That(handler.Request?.RequestUri?.Host, Is.EqualTo("172.30.0.1"));
    }

    [Test]
    public async Task Gateway_ForwardsBodyWhenRequestDoesNotDeclareContentLength()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler();
        await using var gateway = await StartGatewayAsync(appId, targetId, handler);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            gateway.GetUri("/stream"))
        {
            Content = new UnknownLengthContent("streamed body")
        };
        using var response = await client.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(handler.Body, Is.EqualTo("streamed body"));
        });
    }

    [Test]
    public async Task GatewayReturnsUpstreamRedirectWithoutFollowingIt()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler
        {
            ResponseFactory = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("/next?from=runtime", UriKind.Relative) },
                Content = new StringContent("redirect body")
            })
        };
        await using var gateway = await StartGatewayAsync(appId, targetId, handler);

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await client.GetAsync(gateway.GetUri("/start"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/next?from=runtime"));
            Assert.That(body, Is.EqualTo("redirect body"));
        });
    }

    [Test]
    public async Task GatewayFiltersHopByHopHeadersAndPreservesEndToEndHeaders()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler
        {
            ResponseFactory = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("response")
                };
                response.Headers.TryAddWithoutValidation("Connection", "X-Upstream-Hop");
                response.Headers.TryAddWithoutValidation("X-Upstream-Hop", "drop");
                response.Headers.TryAddWithoutValidation("X-End-To-End", "preserve-response");
                return Task.FromResult(response);
            }
        };
        await using var gateway = await StartGatewayAsync(appId, targetId, handler);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, gateway.GetUri("/headers"));
        request.Headers.TryAddWithoutValidation("Connection", "X-Request-Hop");
        request.Headers.TryAddWithoutValidation("X-Request-Hop", "drop");
        request.Headers.TryAddWithoutValidation("X-End-To-End", "preserve-request");
        using var response = await client.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(handler.Request, Is.Not.Null);
            Assert.That(handler.Request!.Headers.Contains("Connection"), Is.False);
            Assert.That(handler.Request.Headers.Contains("X-Request-Hop"), Is.False);
            Assert.That(handler.Request.Headers.GetValues("X-End-To-End").Single(),
                Is.EqualTo("preserve-request"));
            Assert.That(response.Headers.Contains("Connection"), Is.False);
            Assert.That(response.Headers.Contains("X-Upstream-Hop"), Is.False);
            Assert.That(response.Headers.GetValues("X-End-To-End").Single(),
                Is.EqualTo("preserve-response"));
        });
    }

    [TestCase("session=abc", "session=abc")]
    [TestCase("automaton_runtime_appA=secretA", null)]
    [TestCase("automaton_runtime_appA=secretA; automaton_runtime_appB=secretB", null)]
    [TestCase("session=abc; automaton_runtime_appA=secretA; preference=dark; automaton_runtime_appB=secretB", "session=abc; preference=dark")]
    [TestCase(" session = abc ; AUTOMATON_RUNTIME_appA = secretA ; preference = dark ", "session = abc; preference = dark")]
    public async Task GatewayFiltersPlatformRuntimeCookies(string incomingCookie, string? expectedCookie)
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var handler = new CapturingHandler();
        await using var gateway = await StartGatewayAsync(appId, targetId, handler);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, gateway.GetUri("/cookies"));
        request.Headers.TryAddWithoutValidation("Cookie", incomingCookie);
        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(handler.Request, Is.Not.Null);
        Assert.That(handler.Request!.Headers.TryGetValues("Cookie", out var values)
            ? values.Single()
            : null, Is.EqualTo(expectedCookie));
    }

    private static async Task<GatewayHarness> StartGatewayAsync(
        Guid appId,
        Guid targetId,
        CapturingHandler handler,
        string? runtimeHostAddress = null)
    {
        var registry = new RunnerRuntimeRegistry();
        registry.Register(appId, targetId, 18_321);
        var gateway = new RunnerRuntimeGatewayService(
            registry,
            new HttpClient(handler),
            NullLogger<RunnerRuntimeGatewayService>.Instance);
        var previousPort = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        var previousRuntimeHostAddress = Environment.GetEnvironmentVariable(
            RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable);
        var port = GetFreePort();
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", port.ToString());
        Environment.SetEnvironmentVariable(
            RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable,
            runtimeHostAddress);

        try
        {
            await gateway.StartAsync(CancellationToken.None);
            return new GatewayHarness(gateway, previousPort, previousRuntimeHostAddress, port, appId, targetId);
        }
        catch
        {
            await gateway.StopAsync(CancellationToken.None);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", previousPort);
            Environment.SetEnvironmentVariable(
                RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable,
                previousRuntimeHostAddress);
            throw;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class GatewayHarness(
        RunnerRuntimeGatewayService gateway,
        string? previousPort,
        string? previousRuntimeHostAddress,
        int port,
        Guid appId,
        Guid targetId) : IAsyncDisposable
    {
        public Uri GetUri(string path) => new(
            $"http://127.0.0.1:{port}/apps/{appId:D}/targets/{targetId:D}{path}");

        public async ValueTask DisposeAsync()
        {
            await gateway.StopAsync(CancellationToken.None);
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", previousPort);
            Environment.SetEnvironmentVariable(
                RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable,
                previousRuntimeHostAddress);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> ResponseFactory { get; init; }
            = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("runtime response")
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await ResponseFactory(request);
        }
    }

    private sealed class UnknownLengthContent(string value) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            await stream.WriteAsync(bytes);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
