using Serilog;
using Automaton.Core.Scripting.Step;
using Automaton.Core.Scripting.Engines;
using Automaton.Runner.Console.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Handler for executing custom steps on the runner with proper output variable extraction
/// </summary>
public class CustomStepExecuteService
{
    private readonly CustomStepExecutor executor;
    private readonly ILogger logger;

    public CustomStepExecuteService(ScriptEngineHost scriptHost)
    {
        this.executor = new CustomStepExecutor(scriptHost);
        this.logger = Log.ForContext<CustomStepExecuteService>();
    }

    /// <summary>
    /// Execute a custom step and return the results with properly extracted output variables
    /// Uses the unified CustomStepExecutor for consistent behavior with Studio
    /// </summary>
    public async Task<CustomStepExecutionResponse> RunCustomStep(CustomStepPayload customStep)
    {
        logger.Debug("Executing custom step {CustomStepId} ({CustomStepName})",
            customStep.Id, customStep.Name);

        System.Console.WriteLine($"▶️  Executing custom step: {customStep.Name}");

        try
        {
            // ✅ Convert input variables - avoid double-encoding strings
            var inputVars = customStep.CodeInputVariables?.ToDictionary(v => v.Name, v => v.Value) ?? [];

            // Convert output variable definitions
            var ouputVariableNames = customStep.CodeOutputVariables?
                .Select(v => v.Name);

            // Execute using unified executor
            var result = await Task.Run(() =>
                executor.Execute(customStep.Code, customStep.Language, inputVars, ouputVariableNames));

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
                    OutputVariables = result.OutputVariables ?? []
                };
            }

            logger.Debug("Custom step {CustomStepId} completed successfully in {ElapsedMs}ms",
                customStep.Id, result.ExecutionTimeMs);

            System.Console.WriteLine($"✅ Custom step completed: {customStep.Name} ({result.ExecutionTimeMs}ms)");

            return new CustomStepExecutionResponse
            {
                Success = true,
                Output = result.Output,
                Error = null,
                Started = result.Started,
                Finished = result.Finished,
                OutputVariables = result.OutputVariables ?? []
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
                OutputVariables = []
            };
        }
    }
}
