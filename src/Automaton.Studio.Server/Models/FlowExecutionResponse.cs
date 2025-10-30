using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Response from flow execution containing status and output variables
/// </summary>
public class FlowExecutionResponse
{
    /// <summary>
    /// Whether the flow executed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Flow ID that was executed
    /// </summary>
    public Guid FlowId { get; set; }

    /// <summary>
    /// Execution status (Success, Error, etc.)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Number of runners that executed the flow
    /// </summary>
    public int RunnerCount { get; set; }

    /// <summary>
    /// When the flow was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// Human-readable message about execution
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Flow output variables - the actual results from the flow
    /// This is what web apps are interested in!
    /// </summary>
    public Dictionary<string, object?> OutputVariables { get; set; } = new();

    /// <summary>
    /// Execution logs (optional, for debugging)
    /// </summary>
  public List<string>? Logs { get; set; }
}
