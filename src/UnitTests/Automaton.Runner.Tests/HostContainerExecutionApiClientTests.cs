using System.Net;
using System.Net.Http.Headers;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HostContainerExecutionApiClientTests
{
    [Test]
    public async Task SendWithRetryAsync_HonorsRateLimitRetryAfterBeforeRepeatingRequest()
    {
        var sends = 0;
        var delays = new List<TimeSpan>();

        using var response = await HostContainerExecutionApiClient.SendWithRetryAsync(
            _ =>
            {
                sends++;
                var reply = new HttpResponseMessage(sends == 1
                    ? HttpStatusCode.TooManyRequests
                    : HttpStatusCode.OK);
                if (sends == 1)
                    reply.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                return Task.FromResult(reply);
            },
            DateTime.UtcNow.AddMinutes(2),
            CancellationToken.None,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sends, Is.EqualTo(2));
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(60) }));
        });
    }

    [Test]
    public async Task SendWithRetryAsync_RetriesServerFailuresWithBoundedBackoff()
    {
        var sends = 0;
        var delays = new List<TimeSpan>();

        using var response = await HostContainerExecutionApiClient.SendWithRetryAsync(
            _ => Task.FromResult(new HttpResponseMessage(
                ++sends == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)),
            DateTime.UtcNow.AddMinutes(1),
            CancellationToken.None,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sends, Is.EqualTo(2));
            Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(2) }));
        });
    }
}
