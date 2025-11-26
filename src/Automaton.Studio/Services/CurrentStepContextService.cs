using Automaton.Studio.Domain;
using Automaton.Core.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services;

/// <summary>
/// Shared service for maintaining current step context across components
/// </summary>
public interface ICurrentStepContextService
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
    event EventHandler<StepContextChangedEventArgs>? StepContextChanged;

    /// <summary>
    /// Event fired when code is updated (e.g., by MCP tools)
    /// </summary>
    event EventHandler<CodeChangedEventArgs>? CodeChanged;
}

/// <summary>
/// Implementation of the current step context service
/// </summary>
public class CurrentStepContextService : ICurrentStepContextService
{
    private CustomStep? _currentStep;
    private Func<Task<string>>? _getCurrentCodeCallback;
    private readonly ILogger<CurrentStepContextService> _logger;

    public event EventHandler<CodeChangedEventArgs>? CodeChanged;

    public CurrentStepContextService(ILogger<CurrentStepContextService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<StepContextChangedEventArgs>? StepContextChanged;

    public void SetCurrentStep(CustomStep customStep, Func<Task<string>>? getCurrentCodeCallback = null)
    {
        _currentStep = customStep;
        _getCurrentCodeCallback = getCurrentCodeCallback;
        
        _logger.LogDebug("Current step context set: {StepId}", customStep.Id);
        
        StepContextChanged?.Invoke(this, new StepContextChangedEventArgs
        {
            StepId = customStep.Id.ToString(),
            Language = customStep.Definition?.Language
        });
    }

    public string? GetCurrentStepId()
    {
        return _currentStep?.Id.ToString();
    }

    public async Task<string> GetCurrentCodeAsync()
    {
        try
        {
            // First try to get code from the live editor callback
            if (_getCurrentCodeCallback != null)
            {
                var liveCode = await _getCurrentCodeCallback();
                if (!string.IsNullOrEmpty(liveCode))
                {
                    return liveCode;
                }
            }
            
            // Fallback to stored code in the step definition
            return _currentStep?.Definition?.Code ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get current code from callback, using fallback");
            return _currentStep?.Definition?.Code ?? string.Empty;
        }
    }

    public IEnumerable<StepVariable>? GetInputVariables()
    {
        return _currentStep?.Definition?.CodeInputVariables;
    }

    public IEnumerable<StepVariable>? GetOutputVariables()
    {
        return _currentStep?.Definition?.CodeOutputVariables;
    }

    public string? GetLanguage()
    {
        return _currentStep?.Definition?.Language;
    }

    public async Task SetCodeAsync(string code)
    {
        if (_currentStep?.Definition != null)
        {
            _currentStep.Definition.Code = code;
            _logger.LogDebug("Code updated for step {StepId}", _currentStep.Id);

            // Notify subscribers that code has changed (e.g., Monaco editor in UI)
            CodeChanged?.Invoke(this, new CodeChangedEventArgs
            {
                StepId = _currentStep.Id.ToString(),
                NewCode = code
            });
        }
        else
        {
            _logger.LogWarning("SetCodeAsync called but _currentStep is null or has no Definition. " +
                "Code length: {CodeLength}. Ensure SetCurrentStep() is called before AI conversation starts.",
                code?.Length ?? 0);
        }
    }

    public async Task<int> CreateInputVariablesAsync(List<(string Name, string Description)> variables)
    {
        if (_currentStep == null) return 0;

        var created = 0;
        foreach (var (name, description) in variables)
        {
            var variable = new Automaton.Core.Models.StepVariable
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                Value = string.Empty
            };

            _currentStep.Definition.CodeInputVariables ??= new List<StepVariable>();
            _currentStep.Definition.CodeInputVariables.Add(variable);
            created++;
        }

        _logger.LogDebug("Created {Count} input variables for step {StepId}", created, _currentStep.Id);
        
        await Task.CompletedTask;
        return created;
    }

    public async Task<int> CreateOutputVariablesAsync(List<(string Name, string Description)> variables)
    {
        if (_currentStep == null) return 0;

        var created = 0;
        foreach (var (name, description) in variables)
        {
            var variable = new Automaton.Core.Models.StepVariable
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                Value = string.Empty
            };

            _currentStep.Definition.CodeOutputVariables ??= new List<StepVariable>();
            _currentStep.Definition.CodeOutputVariables.Add(variable);
            created++;
        }

        _logger.LogDebug("Created {Count} output variables for step {StepId}", created, _currentStep.Id);
        
        await Task.CompletedTask;
        return created;
    }
}

/// <summary>
/// Event arguments for step context changes
/// </summary>
public class StepContextChangedEventArgs : EventArgs
{
    public string? StepId { get; set; }
    public string? Language { get; set; }
}

/// <summary>
/// Event arguments for code changes
/// </summary>
public class CodeChangedEventArgs : EventArgs
{
    public string? StepId { get; set; }
    public string NewCode { get; set; } = string.Empty;
}