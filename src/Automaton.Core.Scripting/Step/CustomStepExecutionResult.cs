namespace Automaton.Core.Scripting.Step;

/// <summary>
/// Result of custom step execution with unified structure.
/// Used by both Runner.Console and Studio.
/// </summary>
public class CustomStepExecutionResult
{
    public bool Success { get; set; }

    public string? Output { get; set; }

    public string? Error { get; set; }

    public DateTime Started { get; set; }

    public DateTime Finished { get; set; }

    public long ExecutionTimeMs { get; set; }

    public Dictionary<string, string>? OutputVariables { get; set; }
}
