using Automaton.Core.Scripting;
using Automaton.Core.Models;
using Serilog;
using System.Diagnostics;

namespace Automaton.Runner.Console.Handlers;

/// <summary>
/// Handler for executing custom steps on the runner
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
    /// Execute a custom step and return the results
    /// </summary>
    public async Task<CustomStepExecutionResponse> RunCustomStep(CustomStepPayload customStep)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = DateTime.UtcNow;
        
        try
        {
            logger.Information("Executing custom step {CustomStepId} ({CustomStepName})", 
                customStep.Id, customStep.Name);
            
            System.Console.WriteLine($"??  Executing custom step: {customStep.Name}");

            var contentType = GetContentType(customStep.Language);
            var resource = new ScriptResource
            {
                ContentType = contentType,
                Content = customStep.Code
            };
            
            var inputVars = customStep.CodeInputVariables?
                .ToDictionary(v => v.Name, v => (object)v.Value) 
                ?? new Dictionary<string, object>();
            
            // Execute the script on a background thread
            var scriptVariables = await Task.Run(() => scriptHost.Execute(resource, inputVars));
            
            stopwatch.Stop();
            
            // Extract output variables
            var outputVars = customStep.CodeOutputVariables?
                .Where(v => scriptVariables.ContainsKey(v.Name))
                .ToDictionary(v => v.Name, v => (object)(scriptVariables[v.Name]?.ToString() ?? string.Empty))
                ?? new Dictionary<string, object>();

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

                System.Console.WriteLine($"? Custom step failed: {customStep.Name}");

                return new CustomStepExecutionResponse
                {
                    Success = false,
                    Output = scriptVariables.ContainsKey("stdout") ? scriptVariables["stdout"]?.ToString() : null,
                    Error = errorMessage,
                    Started = started,
                    Finished = DateTime.UtcNow,
                    OutputVariables = outputVars
                };
            }

            var output = scriptVariables.ContainsKey("stdout") 
                ? scriptVariables["stdout"]?.ToString() 
                : "Custom step executed successfully";

            logger.Information("Custom step {CustomStepId} completed successfully in {ElapsedMs}ms", 
                customStep.Id, stopwatch.ElapsedMilliseconds);
            
            System.Console.WriteLine($"? Custom step completed: {customStep.Name} ({stopwatch.ElapsedMilliseconds}ms)");
            
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
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            logger.Error(ex, "Error executing custom step {CustomStepId} ({CustomStepName})", 
                customStep.Id, customStep.Name);
            
            System.Console.WriteLine($"? Custom step exception: {customStep.Name} - {ex.Message}");
            
            return new CustomStepExecutionResponse
            {
                Success = false,
                Output = null,
                Error = $"Execution exception: {ex.Message}",
                Started = started,
                Finished = DateTime.UtcNow,
                OutputVariables = new Dictionary<string, object>()
            };
        }
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
