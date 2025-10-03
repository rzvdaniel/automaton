using System.Text.Json;

namespace Automaton.Studio.Services;

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
}

/// <summary>
/// Interface for executing custom step code
/// </summary>
public interface ICustomStepExecuteService
{
    /// <summary>
    /// Execute code asynchronously and return detailed results
    /// </summary>
    Task<CodeExecutionResult> ExecuteCustomStepCodeAsync(
        string code, 
        string language, 
        Dictionary<string, object> inputVariables, 
        string stepId);
}