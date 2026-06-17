namespace Automaton.Api.Services;

public class AppsTunnelConfig
{
    /// <summary>
    /// How long (in seconds) the server waits for the runner to respond via SignalR tunnel.
    /// Should be greater than the runner's TunnelForwarder:TimeoutSeconds (default 300s).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 330;
}
