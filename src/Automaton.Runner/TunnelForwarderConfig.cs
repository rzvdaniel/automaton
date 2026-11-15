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

    // Requests, streams and WebSockets one app may have in flight on this Runner at once. More get a 503 so one
    // busy app cannot take the whole Runner.
    public int MaxConcurrentRequestsPerApp { get; set; } = 64;

    // A request over the cap above waits this long for a free slot before it gets the 503, so a page that loads
    // many files at once is queued, not refused. 0 refuses at once.
    public int SlotWaitSeconds { get; set; } = 10;

    // How many requests of one app may wait at once; more get the 503 immediately.
    public int MaxWaitingPerApp { get; set; } = 100;
}
