using Automaton.Core.Scripting;
using Automaton.Core.Models;
using Serilog;
using System.Diagnostics;
using Automaton.Core.Scripting.Step;

namespace Automaton.Runner.Console.Handlers;

/// <summary>
/// Handler for executing custom steps on the runner with proper output variable extraction
/// </summary>
public class CustomStepHandler
{
    private readonly CustomStepExecutor executor;
    private readonly ILogger logger;

    public CustomStepHandler(ScriptEngineHost scriptHost)
    {
        this.executor = new CustomStepExecutor(scriptHost);
        this.logger = Log.ForContext<CustomStepHandler>();
    }

    /// <summary>
    /// Execute a custom step and return the results with properly extracted output variables
    /// Uses the unified CustomStepExecutor for consistent behavior with Studio
    /// </summary>
    public async Task<CustomStepExecutionResponse> RunCustomStep(CustomStepPayload customStep)
    {
        logger.Information("Executing custom step {CustomStepId} ({CustomStepName})",
            customStep.Id, customStep.Name);

        System.Console.WriteLine($"▶️  Executing custom step: {customStep.Name}");

        try
        {
            // Prepare input variables dictionary
            var inputVars = customStep.CodeInputVariables?
                .ToDictionary(v => v.Name, v => (object)v.Value)
                ?? new Dictionary<string, object>();

            // Convert output variable definitions
            var outputVarDefs = customStep.CodeOutputVariables?
                .Select(v => new StepOutputVariableDefinition { Name = v.Name, Description = v.Description })
                .ToList();

            // Execute using unified executor
            var result = await Task.Run(() =>
                executor.Execute(customStep.Code, customStep.Language, inputVars, outputVarDefs));

            // Check for errors
            if (!result.Success)
            {
                logger.Warning("Custom step {CustomStepId} completed with errors: {Error}",
                    customStep.Id, result.Error);

                System.Console.WriteLine($"❌ Custom step failed: {customStep.Name}");

                return new CustomStepExecutionResponse
                {
                    Success = false,
                    Output = result.Output,
                    Error = result.Error,
                    Started = result.Started,
                    Finished = result.Finished,
                    OutputVariables = result.OutputVariables ?? new Dictionary<string, object>()
                };
            }

            logger.Information("Custom step {CustomStepId} completed successfully in {ElapsedMs}ms",
                customStep.Id, result.ExecutionTimeMs);

            System.Console.WriteLine($"✅ Custom step completed: {customStep.Name} ({result.ExecutionTimeMs}ms)");

            return new CustomStepExecutionResponse
            {
                Success = true,
                Output = result.Output,
                Error = null,
                Started = result.Started,
                Finished = result.Finished,
                OutputVariables = result.OutputVariables ?? new Dictionary<string, object>()
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing custom step {CustomStepId} ({CustomStepName})",
                customStep.Id, customStep.Name);

            System.Console.WriteLine($"❌ Custom step exception: {customStep.Name} - {ex.Message}");

            return new CustomStepExecutionResponse
            {
                Success = false,
                Output = string.Empty,
                Error = $"Execution exception: {ex.Message}",
                Started = DateTime.UtcNow,
                Finished = DateTime.UtcNow,
                OutputVariables = new Dictionary<string, object>()
            };
        }
    }
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
