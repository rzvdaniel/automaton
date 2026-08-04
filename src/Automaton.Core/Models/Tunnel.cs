namespace Automaton.Core.Models;

public class TunnelRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }

    /// <summary>
    /// Optional multi-value headers used by browser runtime proxy requests.
    /// The legacy <see cref="Headers"/> property remains for controller calls.
    /// </summary>
    public Dictionary<string, string[]> HeaderValues { get; set; } = [];

    /// <summary>
    /// Optional binary request body. SignalR serializes this as base64.
    /// </summary>
    public byte[]? BodyBytes { get; set; }

    /// <summary>
    /// Selects a fixed runner-local destination. Empty means the legacy FastAPI controller tunnel.
    /// </summary>
    public string? Destination { get; set; }

    public Guid? RuntimeAppId { get; set; }
    public Guid? RuntimeDeploymentTargetId { get; set; }
}

public class TunnelResponse
{
    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }
    public Dictionary<string, string[]> HeaderValues { get; set; } = [];
    public byte[]? BodyBytes { get; set; }
    public string? Error { get; set; }
}

public static class TunnelDestinations
{
    public const string FastApi = "fast-api";
    public const string RuntimeGateway = "runtime-gateway";
}

/// <summary>
/// Parsed body returned by the FastAPI controller endpoint.
/// </summary>
public class TunnelControllerResponse
{
    public bool Success { get; set; }
    public System.Text.Json.JsonElement? Data { get; set; }
    public string? Error { get; set; }
    /// <summary>
    /// Captured stdout/stderr from print() calls in the controller action.
    /// </summary>
    public string? Output { get; set; }
}
