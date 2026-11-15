using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class TunnelLongLivedLimiterTests
{
    [Test]
    public async Task A_waiting_request_gets_the_slot_when_one_is_released()
    {
        var limiter = new TunnelLongLivedLimiter();
        var app = Guid.NewGuid();
        var held = limiter.TryAcquire(app, 1)!;

        var waiting = limiter.TryAcquireAsync(app, 1, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(100);
        Assert.That(waiting.IsCompleted, Is.False);
        held.Dispose();

        Assert.That(await waiting, Is.Not.Null);
    }

    [Test]
    public async Task A_request_gives_up_when_no_slot_frees_in_time()
    {
        var limiter = new TunnelLongLivedLimiter();
        var app = Guid.NewGuid();
        using var held = limiter.TryAcquire(app, 1);

        Assert.That(await limiter.TryAcquireAsync(app, 1, TimeSpan.FromMilliseconds(100), CancellationToken.None), Is.Null);
        Assert.That(limiter.Count(app), Is.EqualTo(1));
    }
}
