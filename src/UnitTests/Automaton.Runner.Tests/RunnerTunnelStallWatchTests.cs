using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerTunnelStallWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void NoOpenConnection_RedialsOnceThenReconnectsTheHub()
    {
        var watch = new RunnerTunnelStallWatch(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(40));

        Assert.That(watch.Observe(0, Start), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(14)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(15)), Is.EqualTo(RunnerTunnelStallAction.RedialTunnel));
        Assert.That(watch.Observe(0, Start.AddSeconds(25)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(40)), Is.EqualTo(RunnerTunnelStallAction.ReconnectHub));
    }

    [Test]
    public void AnOpenConnection_ResetsTheClock()
    {
        var watch = new RunnerTunnelStallWatch(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(40));

        watch.Observe(0, Start);
        Assert.That(watch.Observe(2, Start.AddSeconds(10)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(20)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(34)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(35)), Is.EqualTo(RunnerTunnelStallAction.RedialTunnel));
    }
}
