using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class CliAuthorizationPollingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RateLimitedPollWaitsAndCollectsTheApprovedCredential(bool useDate)
    {
        var requests = 0;
        using var client = CreateClient(() =>
        {
            if (++requests > 1)
                return Json(HttpStatusCode.OK, "{\"status\":\"authorized\",\"apiKey\":\"test-credential\"}");
            var response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = useDate
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(1))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        });
        var elapsed = Stopwatch.StartNew();

        var credential = await LoginCommand.PollForApiKeyAsync(client, Authorization(10));

        Assert.Multiple(() =>
        {
            Assert.That(credential, Is.EqualTo("test-credential"));
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(elapsed.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900)));
        });
    }

    [Test]
    public void AuthorizationExpiryInterruptsRateLimitBackoffWithoutAHeader()
    {
        var requests = 0;
        using var client = CreateClient(() =>
        {
            requests++;
            return Json(HttpStatusCode.TooManyRequests, "{}");
        });
        var elapsed = Stopwatch.StartNew();

        Assert.ThrowsAsync<TimeoutException>(() => LoginCommand.PollForApiKeyAsync(client, Authorization(1)));

        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void NonRateLimitFailuresAreNotRetried()
    {
        var requests = 0;
        using var client = CreateClient(() =>
        {
            requests++;
            return Json(HttpStatusCode.Unauthorized, "{}");
        });

        Assert.ThrowsAsync<HttpRequestException>(() => LoginCommand.PollForApiKeyAsync(client, Authorization(10)));
        Assert.That(requests, Is.EqualTo(1));
    }

    private static CliAuthorizationResponse Authorization(int expiresIn)
        => new(Guid.NewGuid(), "TEST-CODE", "test-verifier", "https://studio.example.test/cli/authorize", expiresIn, 3);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static AutomatonApiClient CreateClient(Func<HttpResponseMessage> respond)
        => new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://api.example.test/") });

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }
}
