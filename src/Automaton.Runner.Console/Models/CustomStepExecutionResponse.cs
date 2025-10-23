namespace Automaton.Runner.Console.Models;

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
    public Dictionary<string, object>? OutputVariables { get; set; }
}
