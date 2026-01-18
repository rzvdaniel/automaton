using System.Text.Json;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Core.Scripting.Step;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Models;

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
    private readonly AppsStepExecutionService _appsStepExecutionService;

    public AppMcpToolProvider(
        ILogger<AppMcpToolProvider> logger,
        AppsService appsService,
        AppFileStorageService appFileStorageService,
        AppStepsService appStepsService,
        CustomStepsService customStepsService,
        UserContextService userContextService,
        IScriptEngineHost scriptHost,
        AppsStepExecutionService appsStepExecutionService)
    {
        _logger = logger;
        _appsService = appsService;
        _appFileStorageService = appFileStorageService;
        _appStepsService = appStepsService;
        _customStepsService = customStepsService;
        _userContextService = userContextService;
        _scriptHost = scriptHost;
        _appsStepExecutionService = appsStepExecutionService;
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
                Name = "save_app_code",
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
                Name = "save_step_code",
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
                Description = "Execute and test a backend step's code LOCALLY with input variables. Use this for quick local testing during development.",
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
                Name = "test_app_step",
                Description = "Execute a backend step on a Runner to verify it works correctly. CRITICAL: Always use this after creating or modifying steps to ensure they work before telling the user the app is complete. This tests the step the same way it will run in production - on an actual Runner. If the test fails, analyze the error and fix the step code, then test again until successful.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step to test"
                        },
                        "inputs": {
                            "type": "object",
                            "description": "Test input variables as key-value pairs (optional)",
                            "additionalProperties": { "type": "string" }
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
            },

            // File-level tools (for multi-file apps)
            new McpTool
            {
                Name = "read_file",
                Description = "Read a specific file from the app (for multi-file apps with separate CSS/JS files)",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file (e.g., 'styles/main.css', 'js/app.js', 'index.html')"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path"]
                }
                """)
            },
            new McpTool
            {
                Name = "write_file",
                Description = "Write a specific file to the app (for multi-file apps). Creates directories as needed. Use for CSS, JS, or component files.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file (e.g., 'styles/main.css', 'js/app.js')"
                        },
                        "content": {
                            "type": "string",
                            "description": "File content to write"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path", "content"]
                }
                """)
            },
            new McpTool
            {
                Name = "delete_file",
                Description = "Delete a specific file from the app (for multi-file apps). Cannot delete index.html.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file to delete (e.g., 'styles/old.css')"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path"]
                }
                """)
            },
            new McpTool
            {
                Name = "list_files",
                Description = "List all files in the app directory (useful for multi-file apps)",
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
                "save_app_code" => await HandleSaveAppCode(arguments, context),
                
                // File-level tools (for multi-file apps)
                "read_file" => await HandleReadFile(arguments, context),
                "write_file" => await HandleWriteFile(arguments, context),
                "delete_file" => await HandleDeleteFile(arguments, context),
                "list_files" => await HandleListFiles(arguments, context),

                // Backend tools
                "create_backend_step" => await HandleCreateBackendStep(arguments, context),
                "get_step_code" => await HandleGetStepCode(arguments),
                "save_step_code" => await HandleSaveStepCode(arguments),
                "suggest_step_variables" => await HandleSuggestStepVariables(arguments),
                "execute_backend_step_code" => await HandleExecuteBackendStepCode(arguments),
                "test_app_step" => await HandleTestAppStep(arguments, context),
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

    private async Task<McpToolResult> HandleReadFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, path);

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"File '{path}' not found"
                };
            }

            var result = new
            {
                path = path,
                content = content,
                size = content.Length,
                lines = content.Split('\n').Length
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to read file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleWriteFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var content = arguments.GetProperty("content").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            await _appFileStorageService.WriteAppFileAsync(userId, appId, path, content);

            var result = new
            {
                path = path,
                size = content.Length,
                lines = content.Split('\n').Length,
                message = $"File '{path}' saved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (InvalidOperationException ex)
        {
            // File extension validation error
            return new McpToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to write file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleDeleteFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Prevent deletion of index.html
            if (path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Cannot delete index.html - it's the main app file. Use save_app_code to modify it instead."
                };
            }

            await _appFileStorageService.DeleteAppFileAsync(userId, appId, path);

            return new McpToolResult
            {
                Success = true,
                Content = $"File '{path}' deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to delete file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleListFiles(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            var files = await _appFileStorageService.GetAppFilesAsync(userId, appId);

            var result = new
            {
                files = files.ToList(),
                count = files.Count(),
                message = $"Found {files.Count()} file(s)"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to list files: {ex.Message}"
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

    private async Task<McpToolResult> HandleTestAppStep(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var stepName = arguments.GetProperty("step_name").GetString()!;
            var appId = ExtractAppId(arguments, context);

            // Parse input variables
            var inputs = new Dictionary<string, string>();
            if (arguments.TryGetProperty("inputs", out var inputsObj))
            {
                foreach (var prop in inputsObj.EnumerateObject())
                {
                    inputs[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
                }
            }

            // Get app info to create PublishedAppInfo
            var app = _appsService.Get(appId);
            if (app == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"App with ID '{appId}' not found"
                };
            }

            // Create PublishedAppInfo for execution service
            var appInfo = new Models.PublishedAppInfo
            {
                Id = app.Id,
                Name = app.Name,
                PublicUrl = app.PublicUrl ?? "",
                RequiresAuthentication = app.RequiresAuthentication,
                OwnerId = _userContextService.GetUserId(),
                ExecutionRunnerId = app.ExecutionRunnerId
            };

            // Execute the step on the runner
            var executionResult = await _appsStepExecutionService.ExecuteStepForAppAsync(
                appInfo,
                stepName,
                inputs,
                null, // Use app's configured runner
                CancellationToken.None);

            if (executionResult.Success)
            {
                var result = new
                {
                    success = true,
                    stepName = executionResult.StepName ?? stepName,
                    status = executionResult.Status,
                    outputVariables = executionResult.OutputVariables,
                    logs = executionResult.Logs,
                    runnerCount = executionResult.RunnerCount,
                    executedAt = executionResult.ExecutedAt,
                    completedAt = executionResult.CompletedAt,
                    message = $"Step '{stepName}' executed successfully on runner"
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
                    stepName = executionResult.StepName ?? stepName,
                    status = executionResult.Status,
                    error = executionResult.Error,
                    logs = executionResult.Logs,
                    message = $"Step '{stepName}' execution failed: {executionResult.Error}"
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
            _logger.LogError(ex, "Error testing app step");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to test app step: {ex.Message}"
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
