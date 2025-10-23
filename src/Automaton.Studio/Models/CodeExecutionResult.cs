using Automaton.Core.Scripting.Engines;

namespace Automaton.Studio.Models;

/// <summary>
/// Result of code execution
/// </summary>
public class CodeExecutionResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string[]? Errors { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
    public string? ExecutionTime { get; set; }

    /// <summary>
    /// Structured error information for AI consumption
    /// </summary>
    public ScriptErrorInfo? ErrorInfo { get; set; }
}
