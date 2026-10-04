using System.Diagnostics;
using System.Net;
using Automaton.Runner.Console.Connection;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HubRateLimitTests
{
    [Test]
    public async Task InitialHubConnection_HonorsRetryAfterAndRecoversWithoutOpeningCircuit()
    {
        await using var fixture = await HubRateLimitTestHost.CreateAsync("1");
        var policy = new ResilientPolicy(0, 1, TimeSpan.FromMinutes(2)).GetPolicy();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await policy.ExecuteAsync(token => fixture.Connection.StartAsync(token), timeout.Token);

        Assert.That(fixture.Connection.State, Is.EqualTo(HubConnectionState.Connected));
        Assert.That(fixture.Attempts, Has.Count.EqualTo(3));
        for (var index = 1; index < fixture.Attempts.Count; index++)
            Assert.That(Stopwatch.GetElapsedTime(fixture.Attempts[index - 1], fixture.Attempts[index]),
                Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task RateLimitedHubRetry_CancelsWithoutAnotherRequest()
    {
        await using var fixture = await HubRateLimitTestHost.CreateAsync("60");
        var policy = new ResilientPolicy(0, 1, TimeSpan.FromMinutes(2)).GetPolicy();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await policy.ExecuteAsync(token => fixture.Connection.StartAsync(token), cancellation.Token));
        Assert.That(fixture.Attempts, Has.Count.EqualTo(1));
    }

    [TestCase("60")]
    [TestCase("http-date")]
    public void HttpRateLimit_PreservesDeltaOrDateDeadline(string header)
    {
        var now = DateTimeOffset.UtcNow;
        using var client = new HttpClient(new HubRateLimitHandler(new ResponseHandler(
            header == "http-date" ? now.AddSeconds(60).ToString("R") : header)));

        var exception = Assert.ThrowsAsync<HubRateLimitException>(async () =>
            await client.GetAsync("http://localhost/negotiate"));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(exception.RetryNotBefore, Is.InRange(now.AddSeconds(59), now.AddSeconds(61)));
        var delay = new JitteredReconnectPolicy(() => 0).NextRetryDelay(new()
        {
            PreviousRetryCount = 0, RetryReason = new InvalidOperationException("wrapped transport", exception)
        });
        Assert.That(delay, Is.GreaterThan(TimeSpan.FromSeconds(30)));
    }

    [TestCase(null)]
    [TestCase("invalid")]
    [TestCase("0")]
    public void MissingInvalidOrElapsedRetryAfter_UsesBoundedLocalBackoff(string? header)
    {
        using var client = new HttpClient(new HubRateLimitHandler(new ResponseHandler(header)));
        var exception = Assert.ThrowsAsync<HubRateLimitException>(async () =>
            await client.GetAsync("http://localhost/negotiate"));

        Assert.That(ConnectionRetryDelay.Calculate(10, exception, 5, 1, DateTimeOffset.UtcNow),
            Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    private sealed class ResponseHandler(string? retryAfter) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        }
    }
}
