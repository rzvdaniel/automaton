namespace Automaton.Runner.Console;

public class TunnelForwarderConfig
{
    // Must stay under the server's SignalR MaximumReceiveMessageSizeBytes (currently 1 MB).
    // Binary runtime responses are base64 encoded by SignalR, so leave headroom for headers.
    public int MaxBodyBytes { get; set; } = 512 * 1024;

    // How long to wait for the local FastAPI process to respond. Set higher for
    // CPU-intensive controllers (e.g. audio/video conversion, ML inference).
    // Default: 300 seconds (5 minutes).
    public int TimeoutSeconds { get; set; } = 300;
}
