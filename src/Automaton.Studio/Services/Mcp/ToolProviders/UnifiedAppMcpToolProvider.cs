using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Studio.Services.Mcp.EditorBridges;
using Automaton.Studio.Services.Mcp.Helpers;
using Automaton.Studio.Domain;
using Automaton.Core.Models;

namespace Automaton.Studio.Services.Mcp.ToolProviders;

/// <summary>
/// Unified MCP tool provider that supports both frontend (App) and backend (Step) operations.
/// Enables AI to create complete full-stack apps with backend Custom Steps in a single interaction.
/// </summary>
public class UnifiedAppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<UnifiedAppMcpToolProvider> _logger;
    private readonly IAppEditorBridge _appEditorBridge;
    private readonly IStepEditorBridge _stepEditorBridge;
    private readonly CustomStepsService _customStepsService;
    private readonly AppStepsService _appStepsService;
    private readonly AiCodeCleanupService _codeCleanupService;

    // Track created steps in this session
    private readonly Dictionary<string, CustomStepContext> _createdSteps = new();
    private Guid? _currentAppId;

    public UnifiedAppMcpToolProvider(
        ILogger<UnifiedAppMcpToolProvider> logger,
        IAppEditorBridge appEditorBridge,
        IStepEditorBridge stepEditorBridge,
        CustomStepsService customStepsService,
        AppStepsService appStepsService,
        AiCodeCleanupService codeCleanupService)
    {
        _logger = logger;
        _appEditorBridge = appEditorBridge;
        _stepEditorBridge = stepEditorBridge;
        _customStepsService = customStepsService;
        _appStepsService = appStepsService;
        _codeCleanupService = codeCleanupService;
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
                Name = "save_app_code",
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
                Description = "Create a new Custom Step for backend logic",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "name": {
                            "type": "string",
                            "description": "Step name in PascalCase (e.g., SendEmailStep)"
                        },
                        "language": {
                            "type": "string",
                            "enum": ["Python", "Batch", "PowerShell"],
                            "description": "Programming language for the step"
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
                Description = "Define input and output variables for a step",
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
                            "description": "Array of input variables",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string" },
                                    "type": { "type": "string", "enum": ["String", "Text", "Number", "Boolean", "Date", "Json"] },
                                    "description": { "type": "string" },
                                    "is_sensitive": { "type": "boolean" }
                                },
                                "required": ["name", "type", "description"]
                            }
                        },
                        "output_variables": {
                            "type": "array",
                            "description": "Array of output variables",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": { "type": "string" },
                                    "type": { "type": "string", "enum": ["String", "Text", "Number", "Boolean", "Date", "Json"] },
                                    "description": { "type": "string" }
                                },
                                "required": ["name", "type", "description"]
                            }
                        }
                    },
                    "required": ["step_name"]
                }
                """)
            },
            new McpTool
            {
                Name = "link_step_to_app",
                Description = "Link a backend step to the current app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "step_name": {
                            "type": "string",
                            "description": "Name of the step to link"
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
                "save_app_code" => await HandleSaveAppCode(arguments),
                
                // Backend tools
                "create_backend_step" => await HandleCreateBackendStep(arguments),
                "get_step_code" => await HandleGetStepCode(arguments),
                "save_step_code" => await HandleSaveStepCode(arguments),
                "suggest_step_variables" => await HandleSuggestStepVariables(arguments),
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
            // Clean up markdown artifacts
            var cleanedCode = CleanupHtmlCode(rawCode);

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
        var language = GetStringArgument(arguments, "language", "Python");
        var description = GetStringArgument(arguments, "description", "");

        if (string.IsNullOrEmpty(name))
        {
            return new McpToolResult { Success = false, Error = "Step name is required" };
        }

        // Validate language
        if (!new[] { "Python", "Batch", "PowerShell" }.Any(l => l.Equals(language, StringComparison.OrdinalIgnoreCase)))
        {
            return new McpToolResult { Success = false, Error = "Language must be Python, Batch, or PowerShell" };
        }

        try
        {
            // Create the step
            var newStep = new Models.NewCustomStep { Name = name };
            var createdStep = await _customStepsService.Create(newStep);

            // Update with additional properties
            createdStep.DisplayName = name;
            createdStep.Description = description;
            createdStep.Definition.Language = language;
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

            _logger.LogInformation("Created backend step: {StepName} ({Language}, ID: {StepId})", 
                name, language, createdStep.Id);

            return new McpToolResult
            {
                Success = true,
                Content = $"Created backend step '{name}' (ID: {createdStep.Id})"
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
                    var varType = varElement.GetStringProperty("type", "String");
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
                    var varType = varElement.GetStringProperty("type", "String");

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

    private async Task<McpToolResult> HandleLinkStepToApp(JsonElement arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");

        if (!_currentAppId.HasValue)
        {
            return new McpToolResult { Success = false, Error = "No app context available" };
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return new McpToolResult { Success = false, Error = $"Step '{stepName}' not found" };
        }

        try
        {
            await _appStepsService.LinkStepAsync(_currentAppId.Value, step.Id);
            
            _logger.LogInformation("Linked step '{StepName}' to app '{AppId}'", stepName, _currentAppId.Value);
            
            return new McpToolResult
            {
                Success = true,
                Content = $"Step '{stepName}' linked to app"
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

    private string CleanupHtmlCode(string rawCode)
    {
        if (string.IsNullOrEmpty(rawCode))
            return rawCode;

        var code = rawCode.Trim();

        // Remove markdown code block markers if present
        if (code.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
        {
            code = code.Substring(7).TrimStart();
        }
        else if (code.StartsWith("```"))
        {
            code = code.Substring(3).TrimStart();
        }

        if (code.EndsWith("```"))
        {
            code = code.Substring(0, code.Length - 3).TrimEnd();
        }

        return code;
    }

    #endregion

    #region Inner Classes

    private class CustomStepContext
    {
        public Guid StepId { get; set; }
        public string Name { get; set; } = "";
        public string Language { get; set; } = "Python";
    }

    #endregion
}
