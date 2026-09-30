using System.Net;
using System.Net.Http.Headers;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InfrastructureResultDeliveryTests
{
    [Test]
    public async Task Retries429AndApiFailureUsingTheSameComputedResult()
    {
        var result = new object(); // Host work is computed before entering delivery.
        var sent = new List<object>();
        var delays = new List<TimeSpan>();
        using var response = await InfrastructureResultDelivery.SendAsync(_ =>
        {
            sent.Add(result);
            var status = sent.Count switch { 1 => HttpStatusCode.TooManyRequests, 2 => HttpStatusCode.ServiceUnavailable, _ => HttpStatusCode.OK };
            var reply = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests)
                reply.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(reply);
        }, CancellationToken.None, (duration, _) => { delays.Add(duration); return Task.CompletedTask; });
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sent, Has.Count.EqualTo(3));
            Assert.That(sent.All(item => ReferenceEquals(item, result)), Is.True);
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2) }));
        });
    }

    [Test]
    public async Task RetriesNetworkFailureAndRequestTimeout()
    {
        var sends = 0;
        using var response = await InfrastructureResultDelivery.SendAsync(_ =>
        {
            sends++;
            return sends switch
            {
                1 => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection lost")),
                2 => Task.FromException<HttpResponseMessage>(new TaskCanceledException("HTTP timeout")),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
            };
        }, CancellationToken.None, (_, _) => Task.CompletedTask);
        Assert.That(sends, Is.EqualTo(3));
    }

    [Test]
    public void RetryAfterDateUsesTheRequestedDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(9));
        Assert.That(InfrastructureResultDelivery.GetRetryDelay(response, now), Is.EqualTo(TimeSpan.FromSeconds(9)));
        Assert.That(InfrastructureResultDelivery.GetRetryDelay(response, now.AddSeconds(10)), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task DoesNotRetryPermanentRejection()
    {
        using var response = await InfrastructureResultDelivery.SendAsync(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)), CancellationToken.None,
            (_, _) => throw new AssertionException("Permanent rejection must not retry."));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void CancellationInterruptsRetryAfterWithoutAnotherSend()
    {
        using var cancellation = new CancellationTokenSource();
        var sends = 0;
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await InfrastructureResultDelivery.SendAsync(_ =>
            {
                sends++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            }, cancellation.Token, (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }));
        Assert.That(sends, Is.EqualTo(1));
    }
}
