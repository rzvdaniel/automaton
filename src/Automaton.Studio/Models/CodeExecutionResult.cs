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
    public Dictionary<string, string>? OutputVariables { get; set; }
    public string? ExecutionTime { get; set; }
    public ScriptErrorInfo? ErrorInfo { get; set; }
}
