namespace Automaton.Api.Config;

/// <summary>
/// Limits browser requests that cross the SignalR runner tunnel. Bodies are bounded rather than streamed.
/// </summary>
public sealed class RuntimeTunnelConfig
{
    public int MaxBodyBytes { get; set; } = 512 * 1024;
    public int TimeoutSeconds { get; set; } = 45;
}
