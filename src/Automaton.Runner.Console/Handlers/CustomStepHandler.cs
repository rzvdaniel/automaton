using Automaton.Core.Scripting;
using Automaton.Core.Models;
using Serilog;
using System.Diagnostics;

namespace Automaton.Runner.Console.Handlers;

/// <summary>
/// Handler for executing custom steps on the runner with proper output variable extraction
/// </summary>
public class CustomStepHandler
{
    private readonly ScriptEngineHost scriptHost;
    private readonly ILogger logger;

    public CustomStepHandler(ScriptEngineHost scriptHost)
    {
        this.scriptHost = scriptHost;
        this.logger = Log.ForContext<CustomStepHandler>();
    }

    /// <summary>
    /// Execute a custom step and return the results with properly extracted output variables
    /// </summary>
    public async Task<CustomStepExecutionResponse> RunCustomStep(CustomStepPayload customStep)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = DateTime.UtcNow;
        var consoleOutput = new System.Text.StringBuilder();
        
        try
        {
            logger.Information("Executing custom step {CustomStepId} ({CustomStepName})", 
                customStep.Id, customStep.Name);
            
            System.Console.WriteLine($"▶️  Executing custom step: {customStep.Name}");

            var contentType = GetContentType(customStep.Language);
            var resource = new ScriptResource
            {
                ContentType = contentType,
                Content = customStep.Code
            };
            
            // Prepare input variables dictionary
            var inputVars = customStep.CodeInputVariables?
                .ToDictionary(v => v.Name, v => (object)v.Value) 
                ?? new Dictionary<string, object>();
            
            // Capture console output during execution
            EventHandler<string> outputHandler = (sender, text) =>
            {
                consoleOutput.Append(text);
            };
            
            scriptHost.ScriptTextWritten += outputHandler;
            
            try
            {
                // Execute the script
                var scriptVariables = await Task.Run(() => scriptHost.Execute(resource, inputVars));
                
                stopwatch.Stop();
                
                // **KEY FIX**: Extract output variables based on CodeOutputVariables definitions
                // This ensures output variables are properly matched and returned
                var outputVars = ExtractOutputVariables(customStep.CodeOutputVariables, scriptVariables);

                // Check for errors
                var hasErrors = scriptVariables.ContainsKey("error") || 
                               scriptVariables.ContainsKey("stderr");
                
                if (hasErrors)
                {
                    var errorMessage = scriptVariables.ContainsKey("error") 
                        ? scriptVariables["error"]?.ToString() 
                        : scriptVariables["stderr"]?.ToString();

                    logger.Warning("Custom step {CustomStepId} completed with errors: {Error}", 
                        customStep.Id, errorMessage);

                    System.Console.WriteLine($"❌ Custom step failed: {customStep.Name}");

                    return new CustomStepExecutionResponse
                    {
                        Success = false,
                        Output = consoleOutput.ToString(), // Use captured console output
                        Error = errorMessage,
                        Started = started,
                        Finished = DateTime.UtcNow,
                        OutputVariables = outputVars // Include partial output variables even on error
                    };
                }

                // Use captured console output instead of default message
                var output = consoleOutput.Length > 0 
                    ? consoleOutput.ToString() 
                    : "Custom step executed successfully";

                logger.Information("Custom step {CustomStepId} completed successfully in {ElapsedMs}ms", 
                    customStep.Id, stopwatch.ElapsedMilliseconds);
                
                System.Console.WriteLine($"✅ Custom step completed: {customStep.Name} ({stopwatch.ElapsedMilliseconds}ms)");
                
                return new CustomStepExecutionResponse
                {
                    Success = true,
                    Output = output,
                    Error = null,
                    Started = started,
                    Finished = DateTime.UtcNow,
                    OutputVariables = outputVars
                };
            }
            finally
            {
                scriptHost.ScriptTextWritten -= outputHandler;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            logger.Error(ex, "Error executing custom step {CustomStepId} ({CustomStepName})", 
                customStep.Id, customStep.Name);
            
            System.Console.WriteLine($"❌ Custom step exception: {customStep.Name} - {ex.Message}");
            
            return new CustomStepExecutionResponse
            {
                Success = false,
                Output = consoleOutput.ToString(), // Include any captured output before the exception
                Error = $"Execution exception: {ex.Message}",
                Started = started,
                Finished = DateTime.UtcNow,
                OutputVariables = new Dictionary<string, object>()
            };
        }
    }

    /// <summary>
    /// Extract output variables from script execution results
    /// This matches the Studio's logic for consistent variable handling
    /// </summary>
    private Dictionary<string, object> ExtractOutputVariables(
        List<StepVariable> outputVariableDefinitions,
        IDictionary<string, dynamic> scriptVariables)
    {
        var outputVariables = new Dictionary<string, object>();

        if (outputVariableDefinitions == null || !outputVariableDefinitions.Any())
        {
            return outputVariables;
        }

        foreach (var outputVar in outputVariableDefinitions)
        {
            // Try to find the variable in script results by name
            if (scriptVariables.ContainsKey(outputVar.Name))
            {
                var value = scriptVariables[outputVar.Name];
                outputVariables[outputVar.Name] = value?.ToString() ?? string.Empty;
                logger.Debug("Output variable {VariableName} = {Value}", outputVar.Name, value);
            }
            else
            {
                // Variable not set by script - use empty string as default
                outputVariables[outputVar.Name] = string.Empty;
                logger.Debug("Output variable {VariableName} not found in script results", outputVar.Name);
            }
        }

        return outputVariables;
    }
    
    private string GetContentType(string language) => language?.ToLowerInvariant() switch
    {
        "python" => "text/x-python",
        "batch" => "text/x-batch",
        "powershell" => "text/x-powershell",
        "javascript" => "text/javascript",
        _ => "text/x-python"
    };
}

/// <summary>
/// Payload for custom step execution (subset of CustomStep model)
/// </summary>
public class CustomStepPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string Language { get; set; }
    public List<StepVariable> CodeInputVariables { get; set; }
    public List<StepVariable> CodeOutputVariables { get; set; }
}

/// <summary>
/// Response from runner after executing a custom step
/// </summary>
public class CustomStepExecutionResponse
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
}
