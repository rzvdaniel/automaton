namespace Automaton.Api.Config;

/// <summary>
/// Limits browser requests that cross the SignalR runner tunnel. Request bodies are bounded; response bodies and WebSockets stream in chunks.
/// </summary>
public sealed class RuntimeTunnelConfig
{
    public int MaxBodyBytes { get; set; } = 512 * 1024;
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>Streaming responses (Server-Sent Events, large downloads) and WebSockets one app may hold open at once.</summary>
    public int MaxLongLivedPerApp { get; set; } = 20;

    /// <summary>A streaming response or WebSocket with no traffic for this long is closed.</summary>
    public int StreamIdleTimeoutSeconds { get; set; } = 300;
}
