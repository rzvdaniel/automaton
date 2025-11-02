namespace Automaton.Studio.Server.Models;

/// <summary>
/// Result of executing a custom step on a runner
/// </summary>
public class RunnerCustomStepResult
{
    public Guid CustomStepId { get; set; }
    public Guid RunnerId { get; set; }
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, string>? OutputVariables { get; set; }
}

/// <summary>
/// Response from runner after executing a custom step
/// </summary>
public class CustomStepExecutionResponse
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, string>? OutputVariables { get; set; }
}
