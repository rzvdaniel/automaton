using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Studio.Server.Models;
using Automaton.Core.Scripting.Step;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Models;
using Automaton.Steps.Enums;

namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Server-side implementation of MCP tools for Web App editing with backend Custom Steps support
/// </summary>
public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<AppMcpToolProvider> _logger;
    private readonly AppsService _appsService;
    private readonly AppFileStorageService _appFileStorageService;
    private readonly AppStepsService _appStepsService;
    private readonly CustomStepsService _customStepsService;
    private readonly UserContextService _userContextService;
    private readonly IScriptEngineHost _scriptHost;

    public const string SaveAppCodeToolName = "save_app_code";
    public const string SaveStepCodeToolName = "save_step_code";

    public AppMcpToolProvider(
        ILogger<AppMcpToolProvider> logger,
        AppsService appsService,
        AppFileStorageService appFileStorageService,
        AppStepsService appStepsService,
        CustomStepsService customStepsService,
        UserContextService userContextService,
        IScriptEngineHost scriptHost)
    {
        _logger = logger;
        _appsService = appsService;
        _appFileStorageService = appFileStorageService;
        _appStepsService = appStepsService;
        _customStepsService = customStepsService;
        _userContextService = userContextService;
        _scriptHost = scriptHost;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        // Supported languages for custom steps
        var supportedLanguages = "\"Python\", \"PowerShell\", \"Batch\", \"JavaScript\"";

        var tools = new List<McpTool>
        {
            // Frontend tools
            new McpTool
            {
                Name = "get_current_code",
                Description = "Get the current HTML/CSS/JavaScript code from the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    }
                }
                """)
            },
            new McpTool
            {
                Name = SaveAppCodeToolName,
                Description = "Save HTML/CSS/JavaScript code to the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "code": {
                            "type": "string",
                            "description": "The HTML/CSS/JavaScript code to save"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["code"]
                }
                """)
            },

            // Backend step tools
            new McpTool
            {
                Name = "create_backend_step",
                Description = "Create a new Custom Step for backend logic and automatically link it to the current app. The step will execute on a Runner. No need to call link_step_to_app after creation.",
                InputSchema = JsonDocument.Parse($$"""
                {
                    "type": "object",
                    "properties": {
                        "name": {
                            "type": "string",
                            "description": "Step name in PascalCase (e.g., SendEmailStep)"
                        },
                        "language": {
                            "type": "string",
                            "enum": [{{supportedLanguages}}],
                            "description": "Programming language for the step (Python recommended for most use cases)"
                        },
                        "description": {
                            "type": "string",
                            "description": "Description of what the step does"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["name", "language"]
                }
                """)
            },
            new McpTool
            {
                Name = "get_step_code",
                Description = "Get the current code from a backend step",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step to read"
                        }
                    },
                    "required": ["step_name"]
                }
                """)
            },
            new McpTool
            {
                Name = SaveStepCodeToolName,
                Description = "Save code to a backend step",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step to save to"
                        },
                        "code": {
                            "type": "string",
                            "description": "The code to save"
                        }
                    },
                    "required": ["step_name", "code"]
                }
                """)
            },
            new McpTool
            {
                Name = "suggest_step_variables",
                Description = "Define input and output variables for a step. Variables store data passed to/from the step code.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step"
                        },
                        "input_variables": {
                            "type": "array",
                            "description": "Array of input variables that the step code will receive",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string", "description": "Variable name (e.g., 'api_key', 'user_input')" },
                                    "description": { "type": "string", "description": "What this variable is used for" },
                                    "is_sensitive": { "type": "boolean", "description": "True for secrets like API keys or passwords (will be encrypted)" }
                                },
                                "required": ["name", "description"]
                            }
                        },
                        "output_variables": {
                            "type": "array",
                            "description": "Array of output variables that the step code will produce",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string", "description": "Variable name (e.g., 'result', 'status')" },
                                    "description": { "type": "string", "description": "What this variable contains" }
                                },
                                "required": ["name", "description"]
                            }
                        }
                    },
                    "required": ["step_name"]
                }
                """)
            },
            new McpTool
            {
                Name = "execute_backend_step_code",
                Description = "Execute and test a backend step's code with input variables. CRITICAL: Always use this to test the step after creating it and before finalizing the app. This verifies the step works correctly and catches errors early.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step to execute"
                        },
                        "input_variables": {
                            "type": "object",
                            "description": "Test input variables as key-value pairs (optional)",
                            "additionalProperties": true
                        }
                    },
                    "required": ["step_name"]
                }
                """)
            },
            new McpTool
            {
                Name = "link_step_to_app",
                Description = "Link an EXISTING step (created outside this app context) to the current app. Note: Steps created with create_backend_step are already automatically linked - this is only for reusing existing steps across multiple apps.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the existing step to link"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["step_name"]
                }
                """)
            },
            new McpTool
            {
                Name = "get_linked_steps",
                Description = "Get list of steps already linked to this app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    }
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
                // Frontend tools
                "get_current_code" => await HandleGetCurrentCode(arguments, context),
                SaveAppCodeToolName => await HandleSaveAppCode(arguments, context),

                // Backend tools
                "create_backend_step" => await HandleCreateBackendStep(arguments, context),
                "get_step_code" => await HandleGetStepCode(arguments),
                SaveStepCodeToolName => await HandleSaveStepCode(arguments),
                "suggest_step_variables" => await HandleSuggestStepVariables(arguments),
                "execute_backend_step_code" => await HandleExecuteBackendStepCode(arguments),
                "link_step_to_app" => await HandleLinkStepToApp(arguments, context),
                "get_linked_steps" => await HandleGetLinkedSteps(arguments, context),

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

    #region Frontend Tool Handlers

    private async Task<McpToolResult> HandleGetCurrentCode(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Read index.html (main app file)
            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, "index.html");

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = "<!-- No code found. Start with a new HTML template -->\n<!DOCTYPE html>\n<html>\n<head>\n    <title>New App</title>\n    <script src=\"https://cdn.tailwindcss.com\"></script>\n</head>\n<body>\n    <!-- Add your content here -->\n</body>\n</html>"
                };
            }

            var result = new
            {
                code = content,
                linesOfCode = content.Split('\n').Length,
                message = "Current HTML code retrieved successfully"
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

    private async Task<McpToolResult> HandleSaveAppCode(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var code = arguments.GetProperty("code").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Save to index.html
            await _appFileStorageService.WriteAppFileAsync(userId, appId, "index.html", code);

            var result = new
            {
                success = true,
                appId = appId.ToString(),
                linesOfCode = code.Split('\n').Length,
                message = "App code saved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving app code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save app code: {ex.Message}"
            };
        }
    }

    #endregion

    #region Backend Step Tool Handlers

    private async Task<McpToolResult> HandleCreateBackendStep(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var name = arguments.GetProperty("name").GetString()!;
            var language = arguments.GetProperty("language").GetString()!;
            var description = arguments.TryGetProperty("description", out var descProp) ? descProp.GetString() : $"Backend step: {name}";
            var appId = ExtractAppId(arguments, context);

            // Create and link the step using AppStepsService
            var linkedStep = await _appStepsService.CreateStepForAppAsync(appId, name);

            // Update language if different from default
            if (language != "Python")
            {
                var step = await _customStepsService.GetForEditingAsync(linkedStep.Id);
                step.Definition.Language = language;
                if (!string.IsNullOrEmpty(description))
                {
                    step.Description = description;
                }
                await _customStepsService.Update(step.Id, step);
            }

            var result = new
            {
                success = true,
                stepId = linkedStep.Id.ToString(),
                name = name,
                language = language,
                message = $"Backend step '{name}' created and linked to app successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating backend step");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to create backend step: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleGetStepCode(JsonElement arguments)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;

            var step = await _customStepsService.GetByNameForExecutionAsync(stepName);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Step '{stepName}' not found"
                };
            }

            var result = new
            {
                stepId = step.Id.ToString(),
                name = step.Name,
                language = step.Definition.Language,
                code = step.Definition.Code ?? "",
                message = $"Code for step '{stepName}' retrieved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting step code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get step code: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleSaveStepCode(JsonElement arguments)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;
            var code = arguments.GetProperty("code").GetString()!;

            var step = await _customStepsService.GetByNameForExecutionAsync(stepName);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Step '{stepName}' not found"
                };
            }

            // Update the step code
            step.Definition.Code = code;
            await _customStepsService.Update(step.Id, step);

            var result = new
            {
                success = true,
                stepId = step.Id.ToString(),
                name = stepName,
                linesOfCode = code.Split('\n').Length,
                message = $"Code for step '{stepName}' saved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving step code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save step code: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleSuggestStepVariables(JsonElement arguments)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;

            var step = await _customStepsService.GetByNameForExecutionAsync(stepName);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Step '{stepName}' not found"
                };
            }

            // Parse input and output variables from arguments
            var inputVars = new List<StepVariable>();
            var outputVars = new List<StepVariable>();

            if (arguments.TryGetProperty("input_variables", out var inputArray))
            {
                foreach (var varElement in inputArray.EnumerateArray())
                {
                    var varName = varElement.GetProperty("name").GetString()!;
                    var varDesc = varElement.GetProperty("description").GetString()!;
                    var isSensitive = varElement.TryGetProperty("is_sensitive", out var sensProp) && sensProp.GetBoolean();

                    inputVars.Add(new StepVariable
                    {
                        Name = varName,
                        Description = varDesc,
                        IsSensitive = isSensitive
                    });
                }
            }

            if (arguments.TryGetProperty("output_variables", out var outputArray))
            {
                foreach (var varElement in outputArray.EnumerateArray())
                {
                    var varName = varElement.GetProperty("name").GetString()!;
                    var varDesc = varElement.GetProperty("description").GetString()!;

                    outputVars.Add(new StepVariable
                    {
                        Name = varName,
                        Description = varDesc
                    });
                }
            }

            // Update the step definition with new variables
            step.Definition.CodeInputVariables = inputVars;
            step.Definition.CodeOutputVariables = outputVars;
            await _customStepsService.Update(step.Id, step);

            var result = new
            {
                success = true,
                stepId = step.Id.ToString(),
                name = stepName,
                inputCount = inputVars.Count,
                outputCount = outputVars.Count,
                message = $"Variables for step '{stepName}' defined successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting step variables");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to suggest step variables: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleExecuteBackendStepCode(JsonElement arguments)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;

            var step = await _customStepsService.GetByNameForExecutionAsync(stepName);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Step '{stepName}' not found"
                };
            }

            // Parse input variables
            var inputVariables = new Dictionary<string, string>();
            if (arguments.TryGetProperty("input_variables", out var inputObj))
            {
                foreach (var prop in inputObj.EnumerateObject())
                {
                    inputVariables[prop.Name] = prop.Value.ToString();
                }
            }

            // Execute the step using CustomStepExecutor
            var executor = new CustomStepExecutor(_scriptHost);
            var outputVarNames = step.Definition.CodeOutputVariables?.Select(v => v.Name) ?? Enumerable.Empty<string>();
            var executionResult = executor.Execute(
                step.Definition.Code ?? "",
                step.Definition.Language,
                inputVariables,
                outputVarNames
            );

            if (executionResult.Success)
            {
                var result = new
                {
                    success = true,
                    stepName = stepName,
                    executionTimeMs = executionResult.ExecutionTimeMs,
                    outputVariables = executionResult.OutputVariables,
                    message = $"Step '{stepName}' executed successfully"
                };

                return new McpToolResult
                {
                    Success = true,
                    Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
                };
            }
            else
            {
                var errorResult = new
                {
                    success = false,
                    stepName = stepName,
                    error = executionResult.Error,
                    executionTimeMs = executionResult.ExecutionTimeMs,
                    message = $"Step '{stepName}' execution failed"
                };

                return new McpToolResult
                {
                    Success = false,
                    Content = JsonSerializer.Serialize(errorResult, new JsonSerializerOptions { WriteIndented = true }),
                    Error = executionResult.Error
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing backend step code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to execute backend step code: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleLinkStepToApp(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;
            var appId = ExtractAppId(arguments, context);

            var step = await _customStepsService.GetByNameForExecutionAsync(stepName);
            if (step == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Step '{stepName}' not found"
                };
            }

            // Link step to app
            await _appStepsService.LinkStepAsync(appId, step.Id);

            var result = new
            {
                success = true,
                stepId = step.Id.ToString(),
                stepName = stepName,
                appId = appId.ToString(),
                message = $"Step '{stepName}' linked to app successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking step to app");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to link step to app: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleGetLinkedSteps(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var appId = ExtractAppId(arguments, context);

            // Get linked steps for the app
            var linkedSteps = await _appStepsService.GetLinkedStepsAsync(appId);

            var steps = linkedSteps.Select(step => new
            {
                stepId = step.Id.ToString(),
                name = step.Name,
                language = step.Language,
                description = step.Description,
                inputCount = step.InputCount,
                outputCount = step.OutputCount
            }).ToList();

            var result = new
            {
                success = true,
                appId = appId.ToString(),
                count = steps.Count,
                steps = steps,
                message = $"Found {steps.Count} step(s) linked to app"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting linked steps");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get linked steps: {ex.Message}"
            };
        }
    }

    #endregion

    #region Helper Methods

    private Guid ExtractAppId(JsonElement arguments, Dictionary<string, object>? context)
    {
        // Try to get appId from arguments first
        if (arguments.TryGetProperty("appId", out var appIdProp))
        {
            var appIdStr = appIdProp.GetString();
            if (!string.IsNullOrEmpty(appIdStr) && Guid.TryParse(appIdStr, out var appId))
            {
                return appId;
            }
        }

        // Fall back to context (resourceId)
        if (context != null && context.TryGetValue("resourceId", out var resourceIdObj))
        {
            if (resourceIdObj is Guid resourceGuid)
            {
                return resourceGuid;
            }
            if (resourceIdObj is string resourceStr && Guid.TryParse(resourceStr, out var resourceId))
            {
                return resourceId;
            }
        }

        throw new InvalidOperationException("No valid appId provided in arguments or context");
    }

    #endregion
}
