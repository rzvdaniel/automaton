using Automaton.Studio.Domain;
using Automaton.Core.Models;

namespace Automaton.Studio.Services.Mcp.EditorBridges;

/// <summary>
/// Shared service for maintaining step context across components
/// </summary>
public interface IStepEditorBridge
{
    /// <summary>
    /// Set the current step being edited
    /// </summary>
    void SetCurrentStep(CustomStep customStep, Func<Task<string>>? getCurrentCodeCallback = null);

    /// <summary>
    /// Get the current step's ID
    /// </summary>
    string? GetCurrentStepId();

    /// <summary>
    /// Get the current step's code
    /// </summary>
    Task<string> GetCurrentCodeAsync();

    /// <summary>
    /// Get the current step's input variables
    /// </summary>
    IEnumerable<StepVariable>? GetInputVariables();

    /// <summary>
    /// Get the current step's output variables
    /// </summary>
    IEnumerable<StepVariable>? GetOutputVariables();

    /// <summary>
    /// Get the current step's language
    /// </summary>
    string? GetLanguage();

    /// <summary>
    /// Update the step's code
    /// </summary>
    Task SetCodeAsync(string code);

    /// <summary>
    /// Create input variables for the current step
    /// </summary>
    Task<int> CreateInputVariablesAsync(List<(string Name, string Description)> variables);

    /// <summary>
    /// Create output variables for the current step
    /// </summary>
    Task<int> CreateOutputVariablesAsync(List<(string Name, string Description)> variables);

    /// <summary>
    /// Event fired when step context changes
    /// </summary>
    event EventHandler<StepEditorBridgeChangedEventArgs>? StepContextChanged;

    /// <summary>
    /// Event fired when code is updated (e.g., by MCP tools)
    /// </summary>
    event EventHandler<CodeChangedEventArgs>? CodeChanged;
}
