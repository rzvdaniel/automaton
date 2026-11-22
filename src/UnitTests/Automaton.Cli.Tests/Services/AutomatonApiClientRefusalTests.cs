using System.Net;
using System.Text;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

public sealed class AutomatonApiClientRefusalTests
{
    [Test]
    public void RefusedRequestMessageIsTheServersReason()
    {
        var error = Refuse(HttpStatusCode.BadRequest,
            "{\"title\":\"Invalid request\",\"status\":400,\"detail\":\"The schedule interval is too short.\"}");
        Assert.That(error.Message, Is.EqualTo("The schedule interval is too short."));
        Assert.That(error.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void RefusedRequestWithoutProblemJsonKeepsTheStatusText()
    {
        var error = Refuse(HttpStatusCode.BadGateway, "bad gateway");
        Assert.That(error.Message, Does.StartWith("API error 502"));
    }

    private static HttpRequestException Refuse(HttpStatusCode status, string body)
    {
        using var http = new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "key");
        return Assert.ThrowsAsync<HttpRequestException>(() => client.DeleteAppAsync("app-1"))!;
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
