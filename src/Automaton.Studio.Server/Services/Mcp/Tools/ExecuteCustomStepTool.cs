using System.Text;
using System.Text.Json;
using Automaton.Core.Scripting;
using Automaton.Studio.Server.Services;

namespace Automaton.Studio.Server.Services.Mcp.Tools;

/// <summary>
/// Tool that allows AI to execute a custom step and retrieve the execution logs
/// </summary>
public class ExecuteCustomStepTool : IMcpTool
{
    private readonly CustomStepsService customStepsService;
    private readonly ScriptEngineHost scriptEngineHost;
    private readonly ILogger<ExecuteCustomStepTool> logger;

    public string Name => "execute_custom_step";
    
    public string Description => "Executes a custom step with optional input values and returns the execution logs and output values. Use this to test if the step code works correctly or to debug issues.";

    public JsonDocument ParametersSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "stepId": {
                "type": "string",
                "description": "The unique identifier (GUID) of the custom step to execute"
            },
            "inputValues": {
                "type": "object",
                "description": "Optional dictionary of input variable names and their values to use for execution",
                "additionalProperties": true
            }
        },
        "required": ["stepId"]
    }
    """);

    public ExecuteCustomStepTool(
        CustomStepsService customStepsService,
        ScriptEngineHost scriptEngineHost,
        ILogger<ExecuteCustomStepTool> logger)
    {
        this.customStepsService = customStepsService;
        this.scriptEngineHost = scriptEngineHost;
        this.logger = logger;
    }

    public async Task<string> ExecuteAsync(string parameters, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var paramsDoc = JsonDocument.Parse(parameters);
            var stepIdString = paramsDoc.RootElement.GetProperty("stepId").GetString();

            if (!Guid.TryParse(stepIdString, out var stepId))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Invalid step ID format"
                });
            }

            // Parse optional input values
            Dictionary<string, object>? inputValues = null;
            if (paramsDoc.RootElement.TryGetProperty("inputValues", out var inputValuesElement))
            {
                inputValues = JsonSerializer.Deserialize<Dictionary<string, object>>(inputValuesElement.GetRawText());
            }

            logger.LogInformation("Executing custom step {StepId} for user {UserId}", stepId, userId);

            var customStep = customStepsService.Get(stepId);
            
            if (customStep == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Custom step not found"
                });
            }

            // Capture execution logs
            var logsBuilder = new StringBuilder();
            void OnScriptTextWritten(object? sender, string text)
            {
                logsBuilder.AppendLine(text);
            }

            scriptEngineHost.ScriptTextWritten += OnScriptTextWritten;

            try
            {
                // Prepare input variables
                var inputs = new Dictionary<string, object>();
                if (customStep.Definition.CodeInputVariables != null)
                {
                    foreach (var inputVar in customStep.Definition.CodeInputVariables)
                    {
                        // Use provided value or default
                        if (inputValues?.ContainsKey(inputVar.Name) == true)
                        {
                            inputs[inputVar.Name] = inputValues[inputVar.Name];
                        }
                        else
                        {
                            // Provide sensible defaults based on type
                            var defaultValue = GetDefaultValueForType(inputVar.Type);
                            if (defaultValue != null)
                            {
                                inputs[inputVar.Name] = defaultValue;
                            }
                        }
                    }
                }

                // Execute the script
                var scriptResource = new Automaton.Core.Scripting.ScriptResource
                {
                    Content = customStep.Definition.Code ?? string.Empty,
                    ContentType = customStep.Definition.Language ?? "Python"
                };
                
                var outputs = await Task.Run(() => 
                    scriptEngineHost.Execute(scriptResource, inputs), 
                    cancellationToken);

                var result = new
                {
                    success = true,
                    stepId = customStep.Id,
                    stepName = customStep.Name,
                    logs = logsBuilder.ToString(),
                    outputs = outputs.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.ToString() ?? "null"
                    )
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            }
            finally
            {
                scriptEngineHost.ScriptTextWritten -= OnScriptTextWritten;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing custom step for user {UserId}", userId);
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    private object? GetDefaultValueForType(Automaton.Core.Enums.VariableType type)
    {
        return type switch
        {
            Automaton.Core.Enums.VariableType.String => "",
            Automaton.Core.Enums.VariableType.Text => "",
            Automaton.Core.Enums.VariableType.Number => 0,
            Automaton.Core.Enums.VariableType.Boolean => false,
            Automaton.Core.Enums.VariableType.Date => DateTime.Now.ToString("yyyy-MM-dd"),
            _ => null
        };
    }
}
