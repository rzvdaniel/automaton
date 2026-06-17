using System.Text.Json;

namespace Automaton.Api.Models;

// ── Filesystem helpers ────────────────────────────────────────────────────────

public record BackendScriptFile(string RelativePath, string ControllerName, byte[] Content, string Language);

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
    /// <summary>Dependency setup output lines captured from the runner during requirements.txt installation.</summary>
    public List<string>? InstallLogs { get; set; }
}

public class AppControllerUploadResult
{
    public bool Success { get; set; }
    public List<string>? Logs { get; set; }
}

public class DeployBackendRequest
{
    public List<string>? RunnerNames { get; set; }
}

public class DeployAppResponse
{
    public bool Success { get; set; }
    public int FrontendFilesSynced { get; set; }
    public int BackendFilesSynced { get; set; }
    public string FrontendAction { get; set; } = "preserved";
    public string BackendAction { get; set; } = "preserved";
    public DeployBackendResponse BackendDeploy { get; set; } = new();
    public string? Message { get; set; }
}

// ── Execution request/response models ────────────────────────────────────────

public class AppControllerActionRequest
{
    public Dictionary<string, JsonElement>? Inputs { get; set; }
    public List<string>? RunnerNames { get; set; }
    public string? TestRunId { get; set; }
    public bool? UseMockBackend { get; set; }
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
