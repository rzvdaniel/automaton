using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RuntimeTunnelTargetCacheTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private static readonly Guid App = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    [Test]
    public async Task A_burst_of_100_parallel_requests_runs_one_lookup()
    {
        var cache = new RuntimeTunnelTargetCache();
        var loads = 0;
        var gate = new TaskCompletionSource();
        async Task<string> Load() { Interlocked.Increment(ref loads); await gate.Task; return "target"; }

        var requests = Enumerable.Range(0, 100).Select(_ => Task.Run(() => cache.GetOrLoadAsync(App, Target, Load))).ToList();
        await Task.Delay(100);
        gate.SetResult();

        Assert.That(await Task.WhenAll(requests), Has.All.EqualTo("target"));
        Assert.That(loads, Is.EqualTo(1));
    }

    [Test]
    public async Task The_lookup_is_repeated_after_the_time_to_live()
    {
        var clock = new ManualClock();
        var cache = new RuntimeTunnelTargetCache(TimeSpan.FromSeconds(3), clock);
        var loads = 0;
        Task<string> Load() => Task.FromResult("v" + Interlocked.Increment(ref loads));

        Assert.That(await cache.GetOrLoadAsync(App, Target, Load), Is.EqualTo("v1"));
        Assert.That(await cache.GetOrLoadAsync(App, Target, Load), Is.EqualTo("v1"));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.That(await cache.GetOrLoadAsync(App, Target, Load), Is.EqualTo("v2"));
    }

    [Test]
    public async Task A_failed_lookup_is_not_remembered()
    {
        var cache = new RuntimeTunnelTargetCache();
        var calls = 0;
        Task<string> Load() => ++calls == 1 ? throw new InvalidOperationException("db gone") : Task.FromResult("ok");

        // The shared lookup fails, the request retries its own once and succeeds.
        Assert.That(await cache.GetOrLoadAsync(App, Target, Load), Is.EqualTo("ok"));
        Assert.That(await cache.GetOrLoadAsync(App, Target, Load), Is.EqualTo("ok"));
    }
}
