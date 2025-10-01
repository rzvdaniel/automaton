using System.Text.Json;
using Automaton.Studio.Server.Services;

namespace Automaton.Studio.Server.Services.Mcp.Tools;

/// <summary>
/// Tool that allows AI to retrieve the current code of a custom step
/// </summary>
public class GetCustomStepCodeTool : IMcpTool
{
    private readonly CustomStepsService customStepsService;
    private readonly ILogger<GetCustomStepCodeTool> logger;

    public string Name => "get_custom_step_code";
    
    public string Description => "Retrieves the current code from a custom step. Use this when you need to read or analyze the existing code of a custom step that is being edited.";

    public JsonDocument ParametersSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "stepId": {
                "type": "string",
                "description": "The unique identifier (GUID) of the custom step"
            }
        },
        "required": ["stepId"]
    }
    """);

    public GetCustomStepCodeTool(CustomStepsService customStepsService, ILogger<GetCustomStepCodeTool> logger)
    {
        this.customStepsService = customStepsService;
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

            logger.LogInformation("Getting code for custom step {StepId} for user {UserId}", stepId, userId);

            var customStep = customStepsService.Get(stepId);
            
            if (customStep == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Custom step not found"
                });
            }

            var result = new
            {
                success = true,
                stepId = customStep.Id,
                stepName = customStep.Name,
                language = customStep.Definition.Language,
                code = customStep.Definition.Code ?? string.Empty,
                inputVariables = customStep.Definition.CodeInputVariables?.Select(v => new
                {
                    name = v.Name,
                    type = v.Type.ToString(),
                    description = v.Description
                })?.ToList() ?? (object)new List<object>(),
                outputVariables = customStep.Definition.CodeOutputVariables?.Select(v => new
                {
                    name = v.Name,
                    type = v.Type.ToString(),
                    description = v.Description
                })?.ToList() ?? (object)new List<object>()
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting custom step code for user {UserId}", userId);
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}
