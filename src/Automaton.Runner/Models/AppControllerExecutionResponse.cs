namespace Automaton.Runner.Console.Models;

/// <summary>
/// Response from runner after executing an app controller action.
/// </summary>
public class AppControllerExecutionResponse
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, string>? OutputVariables { get; set; }
}
