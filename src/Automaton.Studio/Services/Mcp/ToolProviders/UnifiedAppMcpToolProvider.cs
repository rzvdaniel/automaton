using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Studio.Services.Mcp.EditorBridges;
using Automaton.Studio.Services.Mcp.Helpers;
using Automaton.Studio.Domain;
using Automaton.Studio.Models;
using Automaton.Core.Models;

namespace Automaton.Studio.Services.Mcp.ToolProviders;

/// <summary>
/// Event args for when backend steps are modified (created, updated, linked)
/// </summary>
public class BackendStepsChangedEventArgs : EventArgs
{
    public Guid AppId { get; init; }
    public string StepName { get; init; } = "";
    public string ChangeType { get; init; } = ""; // "created", "linked", "updated"
}

/// <summary>
/// Unified MCP tool provider that supports both frontend (App) and backend (Step) operations.
/// Enables AI to create complete full-stack apps with backend Custom Steps in a single interaction.
/// </summary>
public class UnifiedAppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<UnifiedAppMcpToolProvider> _logger;
    private readonly IAppEditorBridge _appEditorBridge;
    private readonly CustomStepsService _customStepsService;
    private readonly AppStepsService _appStepsService;
    private readonly AiCodeCleanupService _codeCleanupService;
    private readonly ICustomStepExecuteService _customStepExecuteService;

    // Track created steps in this session (thread-safe)
    private readonly ConcurrentDictionary<string, CustomStepContext> _createdSteps = new();
    private Guid? _currentAppId;

    /// <summary>
    /// The name of the tool that saves app (frontend) code
    /// </summary>
    public const string SaveAppCodeToolName = "save_app_code";

    /// <summary>
    /// The name of the tool that saves step (backend) code
    /// </summary>
    public const string SaveStepCodeToolName = "save_step_code";

    /// <summary>
    /// Event fired when backend steps are created, updated, or linked to the app.
    /// UI components can subscribe to refresh their step lists.
    /// </summary>
    public event EventHandler<BackendStepsChangedEventArgs>? BackendStepsChanged;

    public UnifiedAppMcpToolProvider(
        ILogger<UnifiedAppMcpToolProvider> logger,
        IAppEditorBridge appEditorBridge,
        IStepEditorBridge stepEditorBridge,
        CustomStepsService customStepsService,
        AppStepsService appStepsService,
        AiCodeCleanupService codeCleanupService,
        ICustomStepExecuteService customStepExecuteService)
    {
        _logger = logger;
        _appEditorBridge = appEditorBridge;
        _customStepsService = customStepsService;
        _appStepsService = appStepsService;
        _codeCleanupService = codeCleanupService;
        _customStepExecuteService = customStepExecuteService;
    }

    /// <summary>
    /// Set the current app context for this tool provider session
    /// </summary>
    public void SetCurrentApp(Guid appId)
    {
        _currentAppId = appId;
        _createdSteps.Clear();
        _logger.LogInformation("Set current app context to {AppId}", appId);
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        // Build language enum dynamically from CustomStepLanguage
        var supportedLanguages = string.Join(", ", CustomStepLanguage.GetSupportedLanguages().Select(l => $"\"{l}\""));

        var tools = new List<McpTool>
        {
            // Frontend tools
            new McpTool
            {
                Name = "get_current_code",
                Description = "Get the current HTML/CSS/JavaScript code from the app editor",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses current context if not provided)"
                        }
                    }
                }
                """)
            },
            new McpTool
            {
                Name = SaveAppCodeToolName,
                Description = "Save HTML/CSS/JavaScript code to the app editor",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "code": {
                            "type": "string",
                            "description": "The HTML/CSS/JavaScript code to save"
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
                    "properties": {}
                }
                """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? contextId = null, Dictionary<string, object>? context = null)
    {
        try
        {
            _logger.LogInformation("Executing tool: {ToolName}", toolName);

            return toolName switch
            {
                // Frontend tools
                "get_current_code" => await HandleGetCurrentCode(arguments),
                SaveAppCodeToolName => await HandleSaveAppCode(arguments),

                // Backend tools
                "create_backend_step" => await HandleCreateBackendStep(arguments),
                "get_step_code" => await HandleGetStepCode(arguments),
                SaveStepCodeToolName => await HandleSaveStepCode(arguments),
                "suggest_step_variables" => await HandleSuggestStepVariables(arguments),
                "execute_backend_step_code" => await HandleExecuteBackendStepCode(arguments),
                "link_step_to_app" => await HandleLinkStepToApp(arguments),
                "get_linked_steps" => await HandleGetLinkedSteps(),

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

    private async Task<McpToolResult> HandleGetCurrentCode(JsonElement arguments)
    {
        try
        {
            var code = await _appEditorBridge.GetCurrentCodeAsync();
            
            if (string.IsNullOrEmpty(code))
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = "<!-- No code found. Start with a new HTML template -->\n<!DOCTYPE html>\n<html>\n<head>\n    <title>New App</title>\n</head>\n<body>\n    <!-- Add your content here -->\n</body>\n</html>"
                };
            }

            var result = new
            {
                code = code,
                linesOfCode = code.Split('\n').Length,
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

    private async Task<McpToolResult> HandleSaveAppCode(JsonElement arguments)
    {
        var rawCode = GetStringArgument(arguments, "code");

        if (string.IsNullOrEmpty(rawCode))
        {
            return new McpToolResult { Success = false, Error = "Code argument is required" };
        }

        try
        {
            // Clean up markdown artifacts using the shared cleanup service
            var cleanedCode = _codeCleanupService.CleanupGeneratedCode(rawCode);

            if (string.IsNullOrWhiteSpace(cleanedCode))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Code extraction failed - no valid HTML found after cleanup"
                };
            }

            await _appEditorBridge.SetCodeAsync(cleanedCode);

            _logger.LogInformation("App code saved ({Length} characters, {Lines} lines)",
                cleanedCode.Length, cleanedCode.Split('\n').Length);

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(new
                {
                    message = "Code saved successfully to App Designer",
                    linesOfCode = cleanedCode.Split('\n').Length,
                    wasCleanedUp = rawCode != cleanedCode
                }, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving app code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save code: {ex.Message}"
            };
        }
    }

    #endregion

    #region Backend Tool Handlers

    private async Task<McpToolResult> HandleCreateBackendStep(JsonElement arguments)
    {
        var name = GetStringArgument(arguments, "name");
        var language = GetStringArgument(arguments, "language", CustomStepLanguage.GetDefaultLanguage());
        var description = GetStringArgument(arguments, "description", "");

        if (string.IsNullOrEmpty(name))
        {
            return new McpToolResult { Success = false, Error = "Step name is required" };
        }

        if (!_currentAppId.HasValue)
        {
            return new McpToolResult { Success = false, Error = "No app context available" };
        }

        try
        {
            // Use AppStepsService to create AND link in one atomic operation
            var linkedStep = await _appStepsService.CreateStepForAppAsync(_currentAppId.Value, name);

            // Load the full step to update language and description
            var createdStep = await _customStepsService.Load(linkedStep.Id);
            createdStep.Definition.Language = language;
            createdStep.Description = description;
            createdStep.VisibleInExplorer = true;
            createdStep.Category = "My Steps";
            await _customStepsService.Update(createdStep);

            // Track the created step
            _createdSteps[name] = new CustomStepContext
            {
                StepId = createdStep.Id,
                Name = name,
                Language = language
            };

            _logger.LogInformation("Created and auto-linked backend step: {StepName} ({Language}, ID: {StepId})",
                name, language, createdStep.Id);

            // Notify subscribers that a step was created and linked
            BackendStepsChanged?.Invoke(this, new BackendStepsChangedEventArgs
            {
                AppId = _currentAppId.Value,
                StepName = name,
                ChangeType = "created_and_linked"
            });

            return new McpToolResult
            {
                Success = true,
                Content = $"Created backend step '{name}' and automatically linked to app (ID: {createdStep.Id})"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backend step: {StepName}", name);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to create step: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleGetStepCode(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        var code = step.Definition?.Code ?? "";
        return new McpToolResult { Success = true, Content = code };
    }

    private async Task<McpToolResult> HandleSaveStepCode(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        var rawCode = GetStringArgument(arguments, "code");

        if (string.IsNullOrEmpty(rawCode))
        {
            return new McpToolResult { Success = false, Error = "Code argument is required" };
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        try
        {
            // Clean up the code
            var cleanedCode = _codeCleanupService.CleanupGeneratedCode(rawCode);

            if (string.IsNullOrWhiteSpace(cleanedCode))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Code extraction failed - no valid code found after cleanup"
                };
            }

            step.Definition.Code = cleanedCode;
            step.Definition.Version++;
            await _customStepsService.Update(step);

            _logger.LogInformation("Saved code to step: {StepName} (v{Version})", 
                stepName, step.Definition.Version);

            return new McpToolResult
            {
                Success = true,
                Content = $"Code saved to step '{stepName}' (version {step.Definition.Version})"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save step code: {StepName}", stepName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save code: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleSuggestStepVariables(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        try
        {
            // Parse input variables
            if (arguments.TryGetProperty("input_variables", out var inputVarsElement))
            {
                var inputVars = new List<StepVariable>();
                foreach (var varElement in inputVarsElement.EnumerateArray())
                {
                    var varName = varElement.GetStringProperty("name");
                    var varDesc = varElement.GetStringProperty("description");
                    var isSensitive = varElement.GetBoolProperty("is_sensitive");

                    // Skip variables without a name
                    if (string.IsNullOrEmpty(varName))
                    {
                        _logger.LogWarning("Skipping input variable with missing name");
                        continue;
                    }

                    inputVars.Add(new StepVariable
                    {
                        Name = varName,
                        Description = varDesc,
                        IsSensitive = isSensitive
                    });
                }

                step.Definition.CodeInputVariables = inputVars;
            }

            // Parse output variables
            if (arguments.TryGetProperty("output_variables", out var outputVarsElement))
            {
                var outputVars = new List<StepVariable>();
                foreach (var varElement in outputVarsElement.EnumerateArray())
                {
                    var varName = varElement.GetStringProperty("name");
                    var varDesc = varElement.GetStringProperty("description");

                    // Skip variables without a name
                    if (string.IsNullOrEmpty(varName))
                    {
                        _logger.LogWarning("Skipping output variable with missing name");
                        continue;
                    }

                    outputVars.Add(new StepVariable
                    {
                        Name = varName,
                        Description = varDesc
                    });
                }

                step.Definition.CodeOutputVariables = outputVars;
            }

            await _customStepsService.Update(step);

            var inputCount = step.Definition.CodeInputVariables?.Count ?? 0;
            var outputCount = step.Definition.CodeOutputVariables?.Count ?? 0;

            _logger.LogInformation("Updated variables for step: {StepName} ({InputCount} inputs, {OutputCount} outputs)",
                stepName, inputCount, outputCount);

            return new McpToolResult
            {
                Success = true,
                Content = $"Updated step '{stepName}' with {inputCount} input(s) and {outputCount} output(s)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update step variables: {StepName}", stepName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to update variables: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleExecuteBackendStepCode(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");

        if (string.IsNullOrEmpty(stepName))
        {
            return new McpToolResult { Success = false, Error = "Step name is required" };
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        try
        {
            var code = step.Definition?.Code ?? "";
            var language = step.Definition?.Language ?? "python";

            if (string.IsNullOrEmpty(code))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Step has no code to execute. Save code first using save_step_code."
                };
            }

            // Prepare input variables
            var inputVariables = new Dictionary<string, string>();
            if (arguments.TryGetProperty("input_variables", out var inputVarsProp))
            {
                foreach (var prop in inputVarsProp.EnumerateObject())
                {
                    inputVariables[prop.Name] = prop.Value.ToString() ?? "";
                }
            }

            // If no input variables provided, use step's defined input variables with empty values
            if (!inputVariables.Any() && step.Definition.CodeInputVariables?.Any() == true)
            {
                foreach (var inputVar in step.Definition.CodeInputVariables)
                {
                    if (!inputVariables.ContainsKey(inputVar.Name))
                    {
                        inputVariables[inputVar.Name] = ""; // Empty test value
                    }
                }
            }

            // Get output variable names for filtering
            var outputVariableNames = step.Definition.CodeOutputVariables?
                .Select(v => v.Name)
                .ToList() ?? new List<string>();

            _logger.LogInformation("Executing backend step: {StepName} ({Language}) with {InputCount} inputs",
                stepName, language, inputVariables.Count);

            // Execute using the same service as StepMcpToolProvider
            var executionResult = await _customStepExecuteService.ExecuteCustomStepCodeAsync(
                code,
                language,
                inputVariables,
                step.Id.ToString(),
                outputVariableNames);

            // For successful tests: return abbreviated result to save tokens
            // For failed tests: return full detailed error information
            if (executionResult.Success)
            {
                // Abbreviated success result - just confirmation and key outputs
                var successResult = new
                {
                    success = true,
                    message = "✓ Test passed",
                    executionTime = executionResult.ExecutionTime ?? "0ms",
                    outputVariables = executionResult.OutputVariables ?? new Dictionary<string, string>()
                };

                return new McpToolResult
                {
                    Success = true,
                    Content = JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true })
                };
            }
            else
            {
                // Detailed error result for failures - include all diagnostic info
                var errorResult = new
                {
                    success = false,
                    output = executionResult.Output ?? "",
                    errors = executionResult.Errors ?? Array.Empty<string>(),
                    errorInfo = executionResult.ErrorInfo != null ? new
                    {
                        errorType = executionResult.ErrorInfo.ErrorType,
                        message = executionResult.ErrorInfo.Message,
                        lineNumber = executionResult.ErrorInfo.LineNumber,
                        columnNumber = executionResult.ErrorInfo.ColumnNumber,
                        stackTrace = executionResult.ErrorInfo.StackTrace,
                        formattedError = executionResult.ErrorInfo.GetFormattedErrorForAI()
                    } : null,
                    outputVariables = executionResult.OutputVariables ?? new Dictionary<string, string>(),
                    executionTime = executionResult.ExecutionTime ?? "0ms",
                    inputVariablesUsed = inputVariables,
                    definedOutputVariables = outputVariableNames,
                    stepInfo = new
                    {
                        name = step.DisplayName,
                        language = language,
                        inputsExpected = step.Definition.CodeInputVariables?.Count ?? 0,
                        outputsExpected = step.Definition.CodeOutputVariables?.Count ?? 0
                    },
                    recommendations = GetCodeRecommendations(executionResult, code)
                };

                return new McpToolResult
                {
                    Success = false,
                    Content = JsonSerializer.Serialize(errorResult, new JsonSerializerOptions { WriteIndented = true }),
                    Error = executionResult.ErrorInfo?.GetFormattedErrorForAI() ??
                            string.Join("\n", executionResult.Errors ?? Array.Empty<string>())
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing backend step: {StepName}", stepName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Execution failed: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}"
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

    private async Task<McpToolResult> HandleLinkStepToApp(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");

        if (!_currentAppId.HasValue)
        {
            return new McpToolResult { Success = false, Error = "No app context available" };
        }

        // Check if this step was created in this session (already auto-linked)
        if (_createdSteps.ContainsKey(stepName))
        {
            return new McpToolResult
            {
                Success = true,
                Content = $"Step '{stepName}' was created in this session and is already linked to the app. No action needed."
            };
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        try
        {
            await _appStepsService.LinkStepAsync(_currentAppId.Value, step.Id);

            _logger.LogInformation("Linked existing step '{StepName}' to app '{AppId}'", stepName, _currentAppId.Value);

            // Notify subscribers that a step was linked
            BackendStepsChanged?.Invoke(this, new BackendStepsChangedEventArgs
            {
                AppId = _currentAppId.Value,
                StepName = stepName,
                ChangeType = "linked_existing"
            });

            return new McpToolResult
            {
                Success = true,
                Content = $"Existing step '{stepName}' linked to app"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to link step: {StepName}", stepName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to link step: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleGetLinkedSteps()
    {
        if (!_currentAppId.HasValue)
        {
            return new McpToolResult { Success = false, Error = "No app context available" };
        }

        try
        {
            var linkedSteps = await _appStepsService.GetLinkedStepsAsync(_currentAppId.Value);

            if (!linkedSteps.Any())
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = "No steps linked to this app yet."
                };
            }

            var stepsInfo = linkedSteps.Select(s =>
                $"- {s.DisplayName} ({s.Language}): {s.InputCount} inputs, {s.OutputCount} outputs");

            return new McpToolResult
            {
                Success = true,
                Content = $"Linked steps:\n{string.Join("\n", stepsInfo)}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get linked steps");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get linked steps: {ex.Message}"
            };
        }
    }

    #endregion

    #region Helper Methods

    private async Task<CustomStep?> ResolveStepAsync(string stepName)
    {
        // First check session-created steps (by ID for efficiency)
        if (_createdSteps.TryGetValue(stepName, out var context))
        {
            return await _customStepsService.Load(context.StepId);
        }

        // Use efficient GetByName API instead of loading all steps
        return await _customStepsService.GetByName(stepName);
    }

    private string GetStringArgument(JsonElement arguments, string key, string defaultValue = "")
    {
        if (arguments.TryGetProperty(key, out var value))
        {
            return value.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    #endregion

    #region Inner Classes

    private class CustomStepContext
    {
        public Guid StepId { get; set; }
        public string Name { get; set; } = "";
        public string Language { get; set; } = CustomStepLanguage.GetDefaultLanguage();
    }

    #endregion
}
