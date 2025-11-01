namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request model for executing a flow from a web app
/// </summary>
public class ExecuteFlowRequest
{
    /// <summary>
    /// Flow ID to execute (as string to support various formats)
    /// </summary>
    public required string FlowId { get; set; }
    
    /// <summary>
    /// Input variables to pass to the flow (maps to Flow.InputVariables)
    /// </summary>
    public Dictionary<string, string>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute the flow on.
    /// If not specified, the first available online runner will be used.
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}
