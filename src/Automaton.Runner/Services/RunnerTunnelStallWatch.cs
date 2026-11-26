namespace Automaton.Runner.Console.Services;

public enum RunnerTunnelStallAction { None, RedialTunnel, ReconnectHub }

/// <summary>
/// Decides what a Runner does when it is connected to the API but has no open tunnel connection (#1778): the API is
/// answering 503 for every app until it has one. First dial the tunnel again, then, if that does not help, connect to
/// the API again from scratch, so no one has to restart the Runner by hand.
/// </summary>
public sealed class RunnerTunnelStallWatch(TimeSpan redialAfter, TimeSpan reconnectAfter)
{
    private DateTimeOffset? noneSince;
    private bool redialed;

    public RunnerTunnelStallAction Observe(int openConnections, DateTimeOffset now)
    {
        if (openConnections > 0)
        {
            noneSince = null;
            redialed = false;
            return RunnerTunnelStallAction.None;
        }

        noneSince ??= now;
        var stalled = now - noneSince.Value;
        if (stalled >= reconnectAfter)
        {
            noneSince = null;
            redialed = false;
            return RunnerTunnelStallAction.ReconnectHub;
        }
        if (stalled >= redialAfter && !redialed)
        {
            redialed = true;
            return RunnerTunnelStallAction.RedialTunnel;
        }
        return RunnerTunnelStallAction.None;
    }
}
