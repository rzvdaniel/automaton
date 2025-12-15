using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Interface for executing custom step code
/// </summary>
public interface ICustomStepExecuteService
{
    /// <summary>
    /// Execute code asynchronously and return detailed results.
    /// Uses CustomStepExecutor for consistency with Runner execution.
    /// </summary>
    /// <param name="code">The code to execute</param>
    /// <param name="language">The programming language (python, javascript, etc.)</param>
    /// <param name="inputVariables">Input variables to inject into the execution context</param>
    /// <param name="stepId">The step ID for logging and context</param>
    /// <param name="outputVariableNames">Optional list of output variable names to filter results. If null, all variables are returned.</param>
    Task<CodeExecutionResult> ExecuteCustomStepCodeAsync(
        string code, 
        string language, 
        Dictionary<string, string> inputVariables, 
        string stepId,
        IEnumerable<string>? outputVariableNames = null);
}