using System.Diagnostics;
using System.Text;

namespace Automaton.Core.Scripting;

/// <summary>
/// Unified service for executing custom steps with consistent error handling across Runner.Console and Studio
/// </summary>
public class CustomStepExecutor
{
    private readonly ScriptEngineHost scriptHost;

    public CustomStepExecutor(ScriptEngineHost scriptHost)
    {
        this.scriptHost = scriptHost;
    }

    /// <summary>
    /// Execute a custom step with unified error handling and output variable extraction
    /// Used by both Runner.Console and Studio to ensure consistent behavior
    /// </summary>
    public CustomStepExecutionResult Execute(string code,
        string language,
        Dictionary<string, object> inputVariables,
        List<StepOutputVariableDefinition>? outputVariableDefinitions = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = DateTime.UtcNow;
        var consoleOutput = new StringBuilder();

        EventHandler<string>? outputHandler = null;

        try
        {
            // Prepare resource
            var contentType = GetContentType(language);
            var resource = new ScriptResource
            {
                ContentType = contentType,
                Content = code
            };

            // Capture console output during execution
            outputHandler = (sender, text) =>
               {
                   consoleOutput.Append(text);
               };

            scriptHost.ScriptTextWritten += outputHandler;

            // Execute the script
            var scriptVariables = scriptHost.Execute(resource, inputVariables);

            stopwatch.Stop();

            // Extract output variables based on definitions
            var outputVars = ExtractOutputVariables(outputVariableDefinitions, scriptVariables);

            // Check for errors
            var hasErrors = scriptVariables.ContainsKey("error") ||
     scriptVariables.ContainsKey("errorOutput") ||
   (consoleOutput.ToString().Contains("ERROR:") && consoleOutput.ToString().Contains("Failed"));

            if (hasErrors)
            {
                var errorMessage = scriptVariables.ContainsKey("error")
                      ? scriptVariables["error"]?.ToString()
                      : scriptVariables.ContainsKey("errorOutput")
                 ? scriptVariables["errorOutput"]?.ToString()
                   : "Script execution produced errors";

                return new CustomStepExecutionResult
                {
                    Success = false,
                    Output = consoleOutput.ToString(),
                    Error = errorMessage,
                    Started = started,
                    Finished = DateTime.UtcNow,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                    OutputVariables = outputVars
                };
            }

            // Use captured console output
            var output = consoleOutput.Length > 0
          ? consoleOutput.ToString()
      : "Custom step executed successfully";

            return new CustomStepExecutionResult
            {
                Success = true,
                Output = output,
                Error = null,
                Started = started,
                Finished = DateTime.UtcNow,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                OutputVariables = outputVars
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new CustomStepExecutionResult
            {
                Success = false,
                Output = consoleOutput.ToString(),
                Error = $"Execution exception: {ex.Message}",
                Started = started,
                Finished = DateTime.UtcNow,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                OutputVariables = new Dictionary<string, object>()
            };
        }
        finally
        {
            if (outputHandler != null)
            {
                scriptHost.ScriptTextWritten -= outputHandler;
            }
        }
    }

    /// <summary>
    /// Extract output variables from script execution results
    /// Ensures consistent variable handling across all execution environments
    /// </summary>
    private Dictionary<string, object> ExtractOutputVariables(
        List<StepOutputVariableDefinition>? outputVariableDefinitions,
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
            }
            else
            {
                // Variable not set by script - use empty string as default
                outputVariables[outputVar.Name] = string.Empty;
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
/// Result of custom step execution with unified structure
/// Used by both Runner.Console and Studio
/// </summary>
public class CustomStepExecutionResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public long ExecutionTimeMs { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
}

/// <summary>
/// Definition of an output variable for extraction
/// </summary>
public class StepOutputVariableDefinition
{
    public string Name { get; set; }
    public string? Description { get; set; }
}
