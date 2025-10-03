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
    /// Event fired when step context changes
    /// </summary>
    event EventHandler<StepContextChangedEventArgs>? StepContextChanged;
}

/// <summary>
/// Implementation of the current step context service
/// </summary>
public class CurrentStepContextService : ICurrentStepContextService
{
    private CustomStep? _currentStep;
    private Func<Task<string>>? _getCurrentCodeCallback;
    private readonly ILogger<CurrentStepContextService> _logger;

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
        }
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