using System.Net;
using System.Net.Http.Headers;
using Automaton.Core.Models;
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

    private static HostContainerExecutionOperationResponse Done() => new(Guid.NewGuid(), true, new());
    private static HostContainerExecutionOperationResponse Pending() => new(Guid.NewGuid(), false, null);

    [Test]
    public async Task PushedResultIsReadWithExactlyOneGetAndNoPolling()
    {
        var notifier = new HostContainerResultNotifier();
        var id = Guid.NewGuid();
        using var waiter = notifier.Register(id);
        var reads = 0;
        var task = HostContainerExecutionApiClient.WaitForPushedResultAsync(
            () => { reads++; return Task.FromResult(Done()); }, waiter.Signaled, notifier.NextConnection,
            DateTime.UtcNow.AddMinutes(5), id);
        await Task.Delay(300); // a polling loop would have read several times by now
        Assert.That(reads, Is.EqualTo(0));
        Assert.That(notifier.Notify(id), Is.True);
        Assert.That((await task).Completed, Is.True);
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public async Task ReconnectReadsOnceAndDeadlineFallbackReadsOnce()
    {
        var notifier = new HostContainerResultNotifier();
        var id = Guid.NewGuid();
        using var waiter = notifier.Register(id);
        var reads = 0;
        var task = HostContainerExecutionApiClient.WaitForPushedResultAsync(
            () => Task.FromResult(++reads >= 2 ? Done() : Pending()), waiter.Signaled, notifier.NextConnection,
            DateTime.UtcNow.AddSeconds(5), id, fallbackLead: TimeSpan.FromSeconds(4.2));
        await Task.Delay(100);
        notifier.ConsumerConnected();
        await Task.Delay(300);
        Assert.That(reads, Is.EqualTo(1), "one read per reconnect, no loop");
        Assert.That((await task).Completed, Is.True);
        Assert.That(reads, Is.EqualTo(2), "the second read is the one near the deadline");
    }

    [Test]
    public void NoResultBeforeTheDeadlineTimesOutAfterOneFallbackRead()
    {
        var notifier = new HostContainerResultNotifier();
        var id = Guid.NewGuid();
        using var waiter = notifier.Register(id);
        var reads = 0;
        Assert.ThrowsAsync<TimeoutException>(() => HostContainerExecutionApiClient.WaitForPushedResultAsync(
            () => { reads++; return Task.FromResult(Pending()); }, waiter.Signaled, notifier.NextConnection,
            DateTime.UtcNow.AddSeconds(1), id, fallbackLead: TimeSpan.FromMilliseconds(500)));
        Assert.That(reads, Is.EqualTo(1));
    }

    [Test]
    public void DuplicateAndOrphanResultMessagesAreDroppedAndMatchIsByOperationAndRunner()
    {
        var notifier = new HostContainerResultNotifier();
        var runner = Guid.NewGuid();
        var id = Guid.NewGuid();
        byte[] Message(Guid op, Guid run) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Automaton.Core.Models.RunnerExecutionResultReady(op, run), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(id, runner), runner, notifier), Is.False, "nobody waits: orphan");
        using (var waiter = notifier.Register(id))
        {
            Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(Guid.NewGuid(), runner), runner, notifier), Is.False);
            Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(id, Guid.NewGuid()), runner, notifier), Is.False, "another Runner");
            Assert.That(waiter.Signaled.IsCompleted, Is.False);
            Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(id, runner), runner, notifier), Is.True);
            Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(id, runner), runner, notifier), Is.False, "duplicate");
            Assert.That(waiter.Signaled.IsCompleted, Is.True);
        }
        Assert.That(RunnerExecutionQueueConsumer.HandleResultReady(Message(id, runner), runner, notifier), Is.False, "waiter gone after a restart or finish");
        Assert.That(RunnerExecutionQueueConsumer.HandleResultReady("not json"u8, runner, notifier), Is.False);
    }
}
