using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

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