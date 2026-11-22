namespace Automaton.Core.Models;

/// <summary>One WebSocket message fragment relayed between the browser, the API and the Runner.</summary>
public class TunnelWebSocketFrame
{
    public string RequestId { get; set; } = string.Empty;

    /// <summary>One of <see cref="TunnelWebSocketFrameKinds"/>.</summary>
    public string Kind { get; set; } = TunnelWebSocketFrameKinds.Text;
    public byte[] Data { get; set; } = [];
    public bool EndOfMessage { get; set; } = true;
    public int? CloseStatus { get; set; }
    public string? CloseDescription { get; set; }
}

public static class TunnelWebSocketFrameKinds
{
    public const string Text = "text";
    public const string Binary = "binary";
    public const string Close = "close";
}

public static class TunnelDestinations
{
    public const string FastApi = "fast-api";
    public const string RuntimeGateway = "runtime-gateway";
    public const string StaticSite = "static-site";
}

/// <summary>
/// Headers the API sets on every request it sends through a Runner tunnel. The Runner trusts them (the tunnel is
/// authenticated) and rebuilds the local destination from them; the API removes any copy a browser sent.
/// </summary>
public static class TunnelHeaders
{
    public const string Destination = "X-Automaton-Tunnel-Destination";
    public const string AppId = "X-Automaton-Tunnel-App";
    public const string TargetId = "X-Automaton-Tunnel-Target";
    public static readonly string[] All = [Destination, AppId, TargetId];
}
