using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerTunnelReconnectStateTests
{
    [Test]
    public void IdleRunnerReconnectsWhenRequested()
    {
        var state = new RunnerTunnelReconnectState();

        Assert.That(state.RequestReconnect(), Is.True);
        Assert.That(state.ShouldReconnect, Is.True);
        Assert.That(state.TryBeginTunnelRequest(), Is.False);
    }

    [Test]
    public void ActiveTunnelFinishesBeforeReconnectAndNewTunnelsAreRejected()
    {
        var state = new RunnerTunnelReconnectState();

        Assert.That(state.TryBeginTunnelRequest(), Is.True);
        Assert.That(state.RequestReconnect(), Is.False);
        Assert.That(state.TryBeginTunnelRequest(), Is.False);
        Assert.That(state.ShouldReconnect, Is.False);

        Assert.That(state.CompleteTunnelRequest(), Is.True);
        Assert.That(state.ShouldReconnect, Is.True);
    }

    [Test]
    public void ReconnectGraceExpiryBoundsAnUnfinishedTunnel()
    {
        var state = new RunnerTunnelReconnectState();
        Assert.That(state.TryBeginTunnelRequest(), Is.True);
        Assert.That(state.RequestReconnect(), Is.False);

        Assert.That(state.ExpireGracePeriod(), Is.True);
        Assert.That(state.ShouldReconnect, Is.True);
    }
}
