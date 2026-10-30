using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Core.Scripting.Step;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Models;
using Automaton.Api.Models;

namespace Automaton.Api.Services.Mcp;

/// <summary>
/// Server-side implementation of MCP tools for Custom Step editing
/// </summary>
public class StepMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<StepMcpToolProvider> _logger;
    private readonly CustomStepsService _customStepsService;
    private readonly IScriptEngineHost _scriptHost;

    public StepMcpToolProvider(
        ILogger<StepMcpToolProvider> logger,
        CustomStepsService customStepsService,
        IScriptEngineHost scriptHost)
    {
        _logger = logger;
        _customStepsService = customStepsService;
        _scriptHost = scriptHost;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = "get_current_code",
                Description = "Get the current code from a step editor",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "stepId": {
                            "type": "string",
                            "description": "The ID of the step to get code from"
                        }
                    },
                    "required": ["stepId"]
                }
                """)
            },
            new McpTool
            {
                Name = "execute_code",
                Description = "Execute code in a specified language and return results",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "code": {
                            "type": "string",
                            "description": "The code to execute"
                        },
                        "language": {
                            "type": "string",
                            "enum": ["python", "javascript", "csharp", "powershell", "bash"],
                            "description": "The programming language"
                        },
                        "stepId": {
                            "type": "string",
                            "description": "The step ID for context"
                        },
                        "inputVariables": {
                            "type": "object",
                            "description": "Input variables for the code execution",
                            "additionalProperties": true
                        }
                    },
                    "required": ["code", "language"]
                }
                """)
            },
            new McpTool
            {
                Name = "save_code",
                Description = "Save generated code to the step",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "stepId": {
                            "type": "string",
                            "description": "The ID of the step"
                        },
                        "code": {
                            "type": "string",
                            "description": "The code to save"
                        }
                    },
                    "required": ["stepId", "code"]
                }
                """)
            },
            new McpTool
            {
                Name = "get_step_variables",
                Description = "Get input and output variable definitions for a step",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "stepId": {
                            "type": "string",
                            "description": "The ID of the step"
                        }
                    },
                    "required": ["stepId"]
                }
                """)
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
            return toolName switch
            {
                "get_current_code" => GetCurrentCode(arguments, contextId),
                "execute_code" => ExecuteCode(arguments),
                "save_code" => await SaveCodeAsync(arguments, contextId),
                "get_step_variables" => GetStepVariables(arguments, contextId),
                _ => new McpToolResult
                {
                    Success = false,
                    Error = $"Unknown tool: {toolName}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Error executing {toolName}: {ex.Message}"
            };
        }
    }

    private McpToolResult GetCurrentCode(JsonElement arguments, string? contextId)
    {
        try
        {
            var stepId = arguments.TryGetProperty("stepId", out var stepIdProp)
                ? Guid.Parse(stepIdProp.GetString()!)
                : Guid.Parse(contextId!);

            var step = _customStepsService.Get(stepId);

            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Step not found or access denied"
                };
            }

            var currentCode = step.Definition?.Code ?? string.Empty;
            
            if (string.IsNullOrEmpty(currentCode))
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = $"# No code found for {stepId}\n# TODO: Add your implementation here"
                };
            }

            // Return code with metadata
            var result = new
            {
                contextId = stepId.ToString(),
                language = step.Definition?.Language ?? "python",
                code = currentCode,
                codeAnalysis = new
                {
                    linesOfCode = currentCode.Split('\n').Length,
                    hasErrorHandling = currentCode.Contains("try") || currentCode.Contains("catch") || currentCode.Contains("except"),
                    hasLogging = currentCode.Contains("INFO:") || currentCode.Contains("WARN:") || currentCode.Contains("ERROR:"),
                    isEmptyOrTemplate = string.IsNullOrWhiteSpace(currentCode) || 
                                       (currentCode.Trim().StartsWith('#') && currentCode.Trim().Split('\n').Length <= 3)
                },
                message = "Current code retrieved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get current code: {ex.Message}"
            };
        }
    }

    private McpToolResult ExecuteCode(JsonElement arguments)
    {
        var code = arguments.GetProperty("code").GetString();
        var language = arguments.GetProperty("language").GetString();
        
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(language))
        {
            return new McpToolResult { Success = false, Error = "Code and language are required" };
        }

        try
        {
            _logger.LogDebug("Executing {Language} code", language);

            // Prepare input variables
            var inputVariables = new Dictionary<string, string>();
            if (arguments.TryGetProperty("inputVariables", out var inputVarsProp))
            {
                foreach (var prop in inputVarsProp.EnumerateObject())
                {
                    inputVariables[prop.Name] = prop.Value.ToString() ?? "";
                }
            }

            // If stepId is provided, get output variable names from step definition
            IEnumerable<string>? outputVariableNames = null;
            if (arguments.TryGetProperty("stepId", out var stepIdProp) && Guid.TryParse(stepIdProp.GetString(), out var stepId))
            {
                var step = _customStepsService.Get(stepId);
                if (step?.Definition?.CodeOutputVariables != null)
                {
                    outputVariableNames = step.Definition.CodeOutputVariables
                        .Select(variable => variable.Name)
                        .OfType<string>();
                }
            }

            // Execute using CustomStepExecutor
            var executor = new CustomStepExecutor(_scriptHost);
            var executionResult = executor.Execute(code, language, inputVariables, outputVariableNames);

            if (executionResult.Success)
            {
                var result = new
                {
                    success = true,
                    output = executionResult.Output,
                    outputVariables = executionResult.OutputVariables,
                    executionTimeMs = executionResult.ExecutionTimeMs,
                    message = "Code executed successfully"
                };

                return new McpToolResult
                {
                    Success = true,
                    Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
                };
            }
            else
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Execution failed: {executionResult.Error}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Execution error: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> SaveCodeAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var stepId = arguments.TryGetProperty("stepId", out var stepIdProp)
                ? Guid.Parse(stepIdProp.GetString()!)
                : Guid.Parse(contextId!);
            
            var code = arguments.GetProperty("code").GetString()!;

            var step = _customStepsService.Get(stepId);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Step not found or access denied"
                };
            }

            // Update step with new code
            step.Definition.Code = code;
            
            await _customStepsService.Update(stepId, new UpdateCustomStepRequest
            {
                Name = step.Name,
                DisplayName = step.DisplayName,
                Description = step.Description,
                MoreInfo = step.MoreInfo,
                Icon = step.Icon,
                Category = step.Category,
                VisibleInExplorer = step.VisibleInExplorer,
                Definition = step.Definition
            });

            return new McpToolResult
            {
                Success = true,
                Content = "Code saved successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save code: {ex.Message}"
            };
        }
    }

    private McpToolResult GetStepVariables(JsonElement arguments, string? contextId)
    {
        try
        {
            var stepId = arguments.TryGetProperty("stepId", out var stepIdProp)
                ? Guid.Parse(stepIdProp.GetString()!)
                : Guid.Parse(contextId!);

            var step = _customStepsService.Get(stepId);

            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Step not found or access denied"
                };
            }

            var result = new
            {
                inputs = step.Definition?.CodeInputVariables ?? new List<StepVariable>(),
                outputs = step.Definition?.CodeOutputVariables ?? new List<StepVariable>()
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting step variables");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get step variables: {ex.Message}"
            };
        }
    }
}
