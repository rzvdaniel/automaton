namespace Automaton.Core.Models;

public class TunnelRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }
}

public class TunnelResponse
{
    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }
    public string? Error { get; set; }
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
