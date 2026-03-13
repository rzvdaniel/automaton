namespace Automaton.Studio.Server.Models;

// ── Filesystem helpers ────────────────────────────────────────────────────────

public record BackendScriptFile(string ControllerName, string Code, string Language);

// ── Deploy response ───────────────────────────────────────────────────────────

public class DeployInfoResponse
{
    public int ScriptCount { get; set; }
    public bool HasRequirements { get; set; }
}

public class DeployBackendResponse
{
    public bool Success { get; set; }
    public int DeployedCount { get; set; }
    public int TotalScripts { get; set; }
    public int RunnerCount { get; set; }
    public bool HasRequirements { get; set; }
    public string? Message { get; set; }
}

public class DeployBackendRequest
{
    public List<string>? RunnerNames { get; set; }
}

// ── Execution request/response models ────────────────────────────────────────

public class AppControllerActionRequest
{
    public Dictionary<string, string>? Inputs { get; set; }
    public List<string>? RunnerNames { get; set; }
}

public class AppControllerActionResponse
{
    public bool Success { get; set; }
    public string? ControllerName { get; set; }
    public string? ActionName { get; set; }
    public string Status { get; set; } = "Unknown";
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public System.Text.Json.JsonElement? Data { get; set; }
    public List<string>? Logs { get; set; }
}

// ── Internal runner result ────────────────────────────────────────────────────

public class RunnerAppControllerResult
{
    public Guid RunnerId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public System.Text.Json.JsonElement? Data { get; set; }
}
