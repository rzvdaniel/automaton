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
        var time = new ImmediateTimeProvider();

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
            time, () => 0);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sends, Is.EqualTo(2));
            Assert.That(time.Delays.Single(), Is.EqualTo(TimeSpan.FromSeconds(60)).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public async Task SendWithRetryAsync_BacksOffOnTheSteppedScheduleForServerFailures()
    {
        var sends = 0;
        var time = new ImmediateTimeProvider();

        using var response = await HostContainerExecutionApiClient.SendWithRetryAsync(
            _ => Task.FromResult(new HttpResponseMessage(
                ++sends <= 5 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)),
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None,
            time, () => 0);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sends, Is.EqualTo(6));
            Assert.That(time.Delays, Is.EqualTo(new[] { 2, 5, 15, 30, 30 }.Select(s => TimeSpan.FromSeconds(s))));
        });
    }

    [Test]
    public async Task SendWithRetryAsync_ReturnsAConflictWithoutRetrying()
    {
        var sends = 0;
        var time = new ImmediateTimeProvider();

        using var response = await HostContainerExecutionApiClient.SendWithRetryAsync(
            _ => { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)); },
            DateTime.UtcNow.AddMinutes(1),
            CancellationToken.None,
            time, () => 0);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(sends, Is.EqualTo(1));
            Assert.That(time.Delays, Is.Empty);
        });
    }

    [Test]
    public void RetrySchedule_StepsUpToTheCeilingWithBoundedJitter()
    {
        var background = Enumerable.Range(0, 9)
            .Select(attempt => RetrySchedule.Delay(attempt, RetrySchedule.BackgroundCeiling).TotalSeconds);
        Assert.Multiple(() =>
        {
            Assert.That(background, Is.EqualTo(new double[] { 2, 5, 15, 30, 60, 120, 300, 300, 300 }));
            Assert.That(RetrySchedule.Delay(6, RetrySchedule.RequestCeiling), Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(RetrySchedule.Delay(0, RetrySchedule.RequestCeiling, jitter: 1), Is.EqualTo(TimeSpan.FromMilliseconds(2200)));
        });
    }

    /// <summary>Real clock, but every timer fires at once and its due time is recorded.</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (Delays) Delays.Add(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
