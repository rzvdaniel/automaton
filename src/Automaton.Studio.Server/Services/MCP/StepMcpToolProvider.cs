using System.Text.Json;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// MCP tool provider for Custom Step editing context
/// </summary>
public class StepMcpToolProvider : IMcpToolProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _stepId;
    private readonly Dictionary<string, object>? _context;

    public StepMcpToolProvider(
        IServiceProvider serviceProvider,
        string? stepId,
        Dictionary<string, object>? context)
    {
        _serviceProvider = serviceProvider;
        _stepId = stepId;
        _context = context;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = "get_current_code",
                Description = "Read the current code of the step",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {}
                }")
            },
            new McpTool
            {
                Name = "save_code",
                Description = "Save new code to the step",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""code"": {
                            ""type"": ""string"",
                            ""description"": ""The code to save""
                        }
                    },
                    ""required"": [""code""]
                }")
            },
            new McpTool
            {
                Name = "get_step_variables",
                Description = "Get the input and output variables defined for this step",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {}
                }")
            },
            new McpTool
            {
                Name = "suggest_variables",
                Description = "Add new input or output variables to the step",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""inputVariables"": {
                            ""type"": ""array"",
                            ""items"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""name"": { ""type"": ""string"" },
                                    ""type"": { ""type"": ""string"", ""enum"": [""string"", ""int"", ""bool"", ""object""] },
                                    ""description"": { ""type"": ""string"" },
                                    ""required"": { ""type"": ""boolean"" }
                                }
                            },
                            ""description"": ""Input variables to add""
                        },
                        ""outputVariables"": {
                            ""type"": ""array"",
                            ""items"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""name"": { ""type"": ""string"" },
                                    ""type"": { ""type"": ""string"", ""enum"": [""string"", ""int"", ""bool"", ""object""] },
                                    ""description"": { ""type"": ""string"" }
                                }
                            },
                            ""description"": ""Output variables to add""
                        }
                    }
                }")
            },
            new McpTool
            {
                Name = "execute_code",
                Description = "Execute the step code to test it",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""inputs"": {
                            ""type"": ""object"",
                            ""description"": ""Input variables to pass to the step""
                        }
                    }
                }")
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        string? contextId = null,
        Dictionary<string, object>? context = null)
    {
        try
        {
            var stepId = contextId ?? _stepId;
            if (string.IsNullOrEmpty(stepId))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Step ID is required"
                };
            }

            if (!Guid.TryParse(stepId, out var stepGuid))
            {
                return new McpToolResult { Success = false, Error = "Invalid step ID" };
            }

            return toolName switch
            {
                "get_current_code" => await GetCurrentCode(stepGuid),
                "save_code" => await SaveCode(stepGuid, arguments),
                "get_step_variables" => await GetStepVariables(stepGuid),
                "suggest_variables" => await SuggestVariables(stepGuid, arguments),
                "execute_code" => await ExecuteCode(stepGuid, arguments),
                _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<McpToolResult> GetCurrentCode(Guid stepId)
    {
        using var scope = _serviceProvider.CreateScope();
        var stepsService = scope.ServiceProvider.GetRequiredService<CustomStepsService>();

        var step = await stepsService.GetAsync(stepId);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = "Step not found" };
        }

        return new McpToolResult
        {
            Success = true,
            Content = $"```{step.Language.ToLowerInvariant()}\n{step.Code}\n```"
        };
    }

    private async Task<McpToolResult> SaveCode(Guid stepId, JsonElement arguments)
    {
        var code = arguments.GetProperty("code").GetString();

        using var scope = _serviceProvider.CreateScope();
        var stepsService = scope.ServiceProvider.GetRequiredService<CustomStepsService>();

        var step = await stepsService.GetAsync(stepId);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = "Step not found" };
        }

        step.Code = code!;
        await stepsService.UpdateAsync(step);

        return new McpToolResult
        {
            Success = true,
            Content = "Code saved successfully"
        };
    }

    private async Task<McpToolResult> GetStepVariables(Guid stepId)
    {
        using var scope = _serviceProvider.CreateScope();
        var stepsService = scope.ServiceProvider.GetRequiredService<CustomStepsService>();

        var step = await stepsService.GetAsync(stepId);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = "Step not found" };
        }

        var result = new
        {
            inputVariables = step.InputVariables ?? new List<Entities.StepVariable>(),
            outputVariables = step.OutputVariables ?? new List<Entities.StepVariable>()
        };

        return new McpToolResult
        {
            Success = true,
            Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        };
    }

    private async Task<McpToolResult> SuggestVariables(Guid stepId, JsonElement arguments)
    {
        using var scope = _serviceProvider.CreateScope();
        var stepsService = scope.ServiceProvider.GetRequiredService<CustomStepsService>();

        var step = await stepsService.GetAsync(stepId);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = "Step not found" };
        }

        // Add input variables
        if (arguments.TryGetProperty("inputVariables", out var inputVars))
        {
            var newInputs = inputVars.Deserialize<List<VariableDefinition>>() ?? new List<VariableDefinition>();
            step.InputVariables ??= new List<Entities.StepVariable>();

            foreach (var v in newInputs)
            {
                if (!step.InputVariables.Any(existing => existing.Name == v.Name))
                {
                    step.InputVariables.Add(new Entities.StepVariable
                    {
                        Name = v.Name,
                        Type = v.Type,
                        Description = v.Description,
                        IsRequired = v.Required
                    });
                }
            }
        }

        // Add output variables
        if (arguments.TryGetProperty("outputVariables", out var outputVars))
        {
            var newOutputs = outputVars.Deserialize<List<VariableDefinition>>() ?? new List<VariableDefinition>();
            step.OutputVariables ??= new List<Entities.StepVariable>();

            foreach (var v in newOutputs)
            {
                if (!step.OutputVariables.Any(existing => existing.Name == v.Name))
                {
                    step.OutputVariables.Add(new Entities.StepVariable
                    {
                        Name = v.Name,
                        Type = v.Type,
                        Description = v.Description
                    });
                }
            }
        }

        await stepsService.UpdateAsync(step);

        return new McpToolResult
        {
            Success = true,
            Content = "Variables updated successfully"
        };
    }

    private async Task<McpToolResult> ExecuteCode(Guid stepId, JsonElement arguments)
    {
        var inputs = arguments.TryGetProperty("inputs", out var inputsProp)
            ? inputsProp.Deserialize<Dictionary<string, object>>()
            : new Dictionary<string, object>();

        // For now, return a placeholder - full execution requires runner
        return await Task.FromResult(new McpToolResult
        {
            Success = true,
            Content = "Step execution requires a connected runner. Please use the Test button in the UI to execute with a runner."
        });
    }

    private class VariableDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public string? Description { get; set; }
        public bool Required { get; set; }
    }
}
