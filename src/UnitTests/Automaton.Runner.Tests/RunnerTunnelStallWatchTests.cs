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
        var watch = new RunnerTunnelStallWatch(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180));

        Assert.That(watch.Observe(0, Start), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(59)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(60)), Is.EqualTo(RunnerTunnelStallAction.RedialTunnel));
        Assert.That(watch.Observe(0, Start.AddSeconds(90)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(180)), Is.EqualTo(RunnerTunnelStallAction.ReconnectHub));
    }

    [Test]
    public void AnOpenConnection_ResetsTheClock()
    {
        var watch = new RunnerTunnelStallWatch(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180));

        watch.Observe(0, Start);
        Assert.That(watch.Observe(2, Start.AddSeconds(50)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(100)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(159)), Is.EqualTo(RunnerTunnelStallAction.None));
        Assert.That(watch.Observe(0, Start.AddSeconds(160)), Is.EqualTo(RunnerTunnelStallAction.RedialTunnel));
    }
}
