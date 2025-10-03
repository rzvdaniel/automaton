using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Studio.Services;
using Automaton.Mcp.Client.Services;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Implementation of MCP tools using Studio's existing services
/// </summary>
public class StudioMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<StudioMcpToolProvider> _logger;
    private readonly ICustomStepExecuteService _executeService;
    private readonly ICurrentStepContextService _stepContextService;
    private readonly Dictionary<string, StepContext> _stepContexts = new();
    
    public StudioMcpToolProvider(
        ILogger<StudioMcpToolProvider> logger,
        ICustomStepExecuteService executeService,
        ICurrentStepContextService stepContextService)
    {
        _logger = logger;
        _executeService = executeService;
        _stepContextService = stepContextService;
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
            },
            new McpTool
            {
                Name = "save_code",
                Description = "Save generated code to a step",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "stepId": {
                            "type": "string",
                            "description": "The ID of the step to save code to"
                        },
                        "code": {
                            "type": "string",
                            "description": "The code to save"
                        },
                        "language": {
                            "type": "string",
                            "description": "The programming language"
                        }
                    },
                    "required": ["stepId", "code", "language"]
                }
                """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? currentStepId = null)
    {
        try
        {
            return toolName switch
            {
                "get_current_code" => await GetCurrentCodeAsync(arguments, currentStepId),
                "execute_code" => await ExecuteCodeAsync(arguments, currentStepId),
                "get_step_variables" => await GetStepVariablesAsync(arguments, currentStepId),
                "save_code" => await SaveCodeAsync(arguments, currentStepId),
                _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Tool execution failed: {ex.Message}" 
            };
        }
    }

    private async Task<McpToolResult> GetCurrentCodeAsync(JsonElement arguments, string? currentStepId)
    {
        var stepId = arguments.TryGetProperty("stepId", out var stepIdProp) 
            ? stepIdProp.GetString() 
            : currentStepId ?? _stepContextService.GetCurrentStepId();
        
        if (string.IsNullOrEmpty(stepId))
        {
            return new McpToolResult { Success = false, Error = "Step ID is required" };
        }

        try
        {
            // Get the actual current code from the Step Designer
            var currentCode = await _stepContextService.GetCurrentCodeAsync();
            var language = _stepContextService.GetLanguage();
            
            if (string.IsNullOrEmpty(currentCode))
            {
                return new McpToolResult 
                { 
                    Success = true, 
                    Content = $"# No code found for step {stepId}\n# TODO: Add your implementation here\nprint('Hello World')" 
                };
            }

            // Return the actual code with metadata
            var result = new
            {
                stepId = stepId,
                language = language ?? "python",
                code = currentCode,
                codeAnalysis = new
                {
                    linesOfCode = currentCode.Split('\n').Length,
                    hasErrorHandling = currentCode.Contains("try") || currentCode.Contains("catch") || currentCode.Contains("except"),
                    hasLogging = currentCode.Contains("INFO:") || currentCode.Contains("WARN:") || currentCode.Contains("ERROR:"),
                    isEmptyOrTemplate = string.IsNullOrWhiteSpace(currentCode) || currentCode.Trim().StartsWith("#") && currentCode.Trim().Split('\n').Length <= 3
                },
                message = "Current code retrieved from Step Designer"
            };

            return new McpToolResult 
            { 
                Success = true, 
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current code for step {StepId}", stepId);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Failed to get current code: {ex.Message}" 
            };
        }
    }

    private async Task<McpToolResult> ExecuteCodeAsync(JsonElement arguments, string? currentStepId)
    {
        var code = arguments.GetProperty("code").GetString();
        var language = arguments.GetProperty("language").GetString();
        var stepId = arguments.TryGetProperty("stepId", out var stepIdProp) 
            ? stepIdProp.GetString() 
            : currentStepId;
        
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(language))
        {
            return new McpToolResult { Success = false, Error = "Code and language are required" };
        }

        try
        {
            _logger.LogInformation("Executing {Language} code for step {StepId}", language, stepId);
            
            // Prepare input variables with realistic test data
            var inputVariables = new Dictionary<string, object>();
            if (arguments.TryGetProperty("inputVariables", out var inputVarsProp))
            {
                foreach (var prop in inputVarsProp.EnumerateObject())
                {
                    inputVariables[prop.Name] = prop.Value.ToString() ?? "";
                }
            }
            else
            {
                // Provide default test data following Studio's patterns
                inputVariables["input_text"] = "Test data for processing";
                inputVariables["input_number"] = 42;
                inputVariables["input_data"] = new Dictionary<string, object> { ["key"] = "value" };
            }

            // Use the existing Studio execution service
            var executionResult = await _executeService.ExecuteCustomStepCodeAsync(
                code, 
                language, 
                inputVariables, 
                stepId ?? Guid.NewGuid().ToString());
            
            // Store updated context
            if (!string.IsNullOrEmpty(stepId))
            {
                _stepContexts[stepId] = new StepContext { Code = code, Language = language };
            }

            // Enhanced result with detailed feedback
            var result = new
            {
                success = executionResult.Success,
                output = executionResult.Output ?? "",
                errors = executionResult.Errors ?? Array.Empty<string>(),
                outputVariables = executionResult.OutputVariables ?? new Dictionary<string, object>(),
                executionTime = executionResult.ExecutionTime ?? "0ms",
                inputVariablesUsed = inputVariables,
                codeAnalysis = new
                {
                    linesOfCode = code?.Split('\n').Length ?? 0,
                    language = language,
                    hasErrorHandling = code?.Contains("try") == true || code?.Contains("catch") == true || code?.Contains("except") == true,
                    hasLogging = code?.Contains("INFO:") == true || code?.Contains("WARN:") == true || code?.Contains("ERROR:") == true
                },
                recommendations = GetCodeRecommendations(executionResult, code)
            };

            return new McpToolResult 
            { 
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {Language} code", language);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Execution failed: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Provide code improvement recommendations based on execution results
    /// </summary>
    private object GetCodeRecommendations(CodeExecutionResult executionResult, string? code)
    {
        var recommendations = new List<string>();

        if (!executionResult.Success)
        {
            recommendations.Add("Add proper error handling with try/catch blocks");
            recommendations.Add("Initialize output variables with default values");
        }

        if (code != null)
        {
            if (!code.Contains("INFO:") && !code.Contains("WARN:") && !code.Contains("ERROR:"))
            {
                recommendations.Add("Add standardized logging with INFO:, WARN:, ERROR: prefixes");
            }

            if (!code.Contains("try") && !code.Contains("except") && !code.Contains("catch"))
            {
                recommendations.Add("Add exception handling to make code more robust");
            }
        }

        if (executionResult.OutputVariables?.Any() != true)
        {
            recommendations.Add("Ensure all expected output variables are assigned at global scope");
        }

        return new
        {
            items = recommendations,
            followsStudioPatterns = recommendations.Count == 0
        };
    }

    private async Task<McpToolResult> GetStepVariablesAsync(JsonElement arguments, string? currentStepId)
    {
        var stepId = arguments.TryGetProperty("stepId", out var stepIdProp) 
            ? stepIdProp.GetString() 
            : currentStepId ?? _stepContextService.GetCurrentStepId();
        
        if (string.IsNullOrEmpty(stepId))
        {
            return new McpToolResult { Success = false, Error = "Step ID is required" };
        }

        try
        {
            // Get real variables from the current step
            var inputVariables = _stepContextService.GetInputVariables();
            var outputVariables = _stepContextService.GetOutputVariables();
            var language = _stepContextService.GetLanguage();

            var variables = new
            {
                stepId = stepId,
                language = language ?? "python",
                executionRules = new
                {
                    inputHandling = "Input variables are pre-injected as globals. Access them directly by name.",
                    outputHandling = "Assign output variables at module/global scope with exact names.",
                    errorHandling = "Initialize outputs with defaults, preserve them on errors.",
                    dataTypes = "Use only JSON-serializable types: strings, numbers, booleans, lists, dicts with string keys."
                },
                inputVariables = inputVariables?.Select(v => new { 
                    name = v.Name, 
                    type = v.Type.ToString(), 
                    description = v.Description ?? "",
                    required = true,
                    example = GetExampleValue(v.Type)
                }).ToArray() ?? Array.Empty<object>(),
                outputVariables = outputVariables?.Select(v => new { 
                    name = v.Name, 
                    type = v.Type.ToString(), 
                    description = v.Description ?? "",
                    required = true,
                    defaultValue = GetDefaultValue(v.Type)
                }).ToArray() ?? Array.Empty<object>(),
                loggingGuidance = new
                {
                    info = "Use 'INFO: message' prefix for informational logs",
                    warning = "Use 'WARN: message' prefix for warnings", 
                    error = "Use 'ERROR: message' prefix for errors",
                    note = "Host parses log streams by these prefixes"
                }
            };

            return new McpToolResult 
            { 
                Success = true, 
                Content = JsonSerializer.Serialize(variables, new JsonSerializerOptions { WriteIndented = true }) 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting step variables for step {StepId}", stepId);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Failed to get step variables: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Get example value for a variable type
    /// </summary>
    private object GetExampleValue(Automaton.Core.Enums.VariableType variableType)
    {
        return variableType switch
        {
            Automaton.Core.Enums.VariableType.String => "example text",
            Automaton.Core.Enums.VariableType.Text => "multi-line text example",
            Automaton.Core.Enums.VariableType.Number => 42,
            Automaton.Core.Enums.VariableType.Boolean => true,
            Automaton.Core.Enums.VariableType.Date => DateTime.Now.ToString("yyyy-MM-dd"),
            _ => "example value"
        };
    }

    /// <summary>
    /// Get default value for a variable type
    /// </summary>
    private object GetDefaultValue(Automaton.Core.Enums.VariableType variableType)
    {
        return variableType switch
        {
            Automaton.Core.Enums.VariableType.String => "",
            Automaton.Core.Enums.VariableType.Text => "",
            Automaton.Core.Enums.VariableType.Number => 0,
            Automaton.Core.Enums.VariableType.Boolean => false,
            Automaton.Core.Enums.VariableType.Date => DateTime.MinValue.ToString("yyyy-MM-dd"),
            _ => ""
        };
    }

    private async Task<McpToolResult> SaveCodeAsync(JsonElement arguments, string? currentStepId)
    {
        var stepId = arguments.TryGetProperty("stepId", out var stepIdProp) 
            ? stepIdProp.GetString() 
            : currentStepId ?? _stepContextService.GetCurrentStepId();
        var code = arguments.GetProperty("code").GetString();
        var language = arguments.GetProperty("language").GetString();
        
        if (string.IsNullOrEmpty(stepId) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(language))
        {
            return new McpToolResult { Success = false, Error = "Step ID, code, and language are required" };
        }

        try
        {
            // Save to the actual step context so it appears in the UI
            await _stepContextService.SetCodeAsync(code);
            
            // Also store in our local context for future reference
            _stepContexts[stepId] = new StepContext { Code = code, Language = language };
            
            _logger.LogInformation("Code saved for step {StepId} in {Language}", stepId, language);

            return new McpToolResult 
            { 
                Success = true, 
                Content = $"Code saved successfully for step {stepId}. The code is now available in the Step Designer." 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving code for step {StepId}", stepId);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Failed to save code: {ex.Message}" 
            };
        }
    }
}

/// <summary>
/// Context for tracking step state
/// </summary>
public class StepContext
{
    public string? Code { get; set; }
    public string? Language { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();
}