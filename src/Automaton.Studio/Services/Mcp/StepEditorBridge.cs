using Automaton.Studio.Domain;
using Automaton.Core.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Implementation of the current step context service
/// </summary>
public class StepEditorBridge : IStepEditorBridge
{
    private CustomStep? _currentStep;
    private Func<Task<string>>? _getCurrentCodeCallback;
    private readonly ILogger<StepEditorBridge> _logger;

    public event EventHandler<CodeChangedEventArgs>? CodeChanged;

    public StepEditorBridge(ILogger<StepEditorBridge> logger)
    {
        _logger = logger;
    }

    public event EventHandler<StepEditorBridgeChangedEventArgs>? StepContextChanged;

    public void SetCurrentStep(CustomStep customStep, Func<Task<string>>? getCurrentCodeCallback = null)
    {
        _currentStep = customStep;
        _getCurrentCodeCallback = getCurrentCodeCallback;
        
        _logger.LogDebug("Current step context set: {StepId}", customStep.Id);
        
        StepContextChanged?.Invoke(this, new StepEditorBridgeChangedEventArgs
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
            // Log diagnostic information to help identify the timing issue
            _logger.LogWarning("SetCodeAsync called but _currentStep is null or has no Definition. " +
                "Code length: {CodeLength}. This typically happens when SetCurrentStep() wasn't called before the AI conversation started, " +
                "or if the Blazor circuit was reset. Check that StepPage.OnInitializedAsync() completes before AI chat interaction.",
                code?.Length ?? 0);
            
            // Even though _currentStep is null, still fire the CodeChanged event with the code
            // This allows any subscribers to still receive the code update
            // The subscriber (StepPage) can use the code directly even without a step ID
            CodeChanged?.Invoke(this, new CodeChangedEventArgs
            {
                StepId = null,  // Unknown step ID - subscriber should use code directly
                NewCode = code
            });
        }
    }

    public async Task<int> CreateInputVariablesAsync(List<(string Name, string Description)> variables)
    {
        if (_currentStep == null) return 0;

        var created = 0;
        foreach (var (name, description) in variables)
        {
            var variable = new StepVariable
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
            var variable = new StepVariable
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
public class StepEditorBridgeChangedEventArgs : EventArgs
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