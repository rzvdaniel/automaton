namespace Automaton.Api.Config;

/// <summary>
/// Limits browser requests that cross a Runner's app tunnel. Bodies stream in both directions, so only long-lived
/// connections are bounded.
/// </summary>
public sealed class RuntimeTunnelConfig
{
    /// <summary>WebSockets one app may hold open at once.</summary>
    public int MaxLongLivedPerApp { get; set; } = 20;

    /// <summary>A request or WebSocket with no traffic for this long is closed.</summary>
    public int StreamIdleTimeoutSeconds { get; set; } = 300;
}
