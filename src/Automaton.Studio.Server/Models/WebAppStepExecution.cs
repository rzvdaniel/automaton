namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to execute a Custom Step from a Web App
/// </summary>
public class WebAppStepExecutionRequest
{
    /// <summary>
    /// Input variables as key-value pairs
    /// </summary>
    public Dictionary<string, string>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute on
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}

/// <summary>
/// Response from Custom Step execution
/// </summary>
public class WebAppStepExecutionResponse
{
    public bool Success { get; set; }
    public Guid StepId { get; set; }
    public string? StepName { get; set; }
    public string Status { get; set; } = "Unknown";
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
    public List<string>? Logs { get; set; }
}

/// <summary>
/// Basic step info for listing
/// </summary>
public class AppStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
}
