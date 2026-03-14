namespace Automaton.Runner.Console;

public class TunnelForwarderConfig
{
    // Must stay under the server's SignalR MaximumReceiveMessageSizeBytes (currently 1 MB).
    // Controller responses are JSON only — files are uploaded directly to the server via
    // POST /api/runner/files and returned as a download URL, so binary content never
    // travels through this tunnel. 512 KB is generous for any JSON response.
    public int MaxBodyBytes { get; set; } = 512 * 1024;

    // How long to wait for the local FastAPI process to respond. Set higher for
    // CPU-intensive controllers (e.g. audio/video conversion, ML inference).
    // Default: 300 seconds (5 minutes).
    public int TimeoutSeconds { get; set; } = 300;
}
