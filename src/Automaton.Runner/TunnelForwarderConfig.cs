namespace Automaton.Runner.Console;

public class TunnelForwarderConfig
{
    // How long a request may sit idle (no bytes either way) before it is cut. Set higher for
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

    // Tunnel connections this Runner keeps open to the API. Each is its own TCP connection, so a lost packet on one
    // does not stall requests on the others.
    public int TunnelConnections { get; set; } = 3;

    // HTTP/2 flow-control windows of the tunnel. Larger windows let one download fill a fast, high-latency link.
    public int Http2StreamWindowBytes { get; set; } = 16 * 1024 * 1024;
    public int Http2ConnectionWindowBytes { get; set; } = 64 * 1024 * 1024;

    // Where the Runner serves each destination; the API only names the destination.
    public string RuntimeGatewayUrl { get; set; } = "http://127.0.0.1:8080";
    public string FastApiUrl { get; set; } = "http://localhost:8001";
}
