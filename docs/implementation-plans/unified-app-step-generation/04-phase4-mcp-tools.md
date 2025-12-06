# Phase 4: MCP Tools Enhancement

## Overview

Enhance the MCP (Model Context Protocol) tools to support unified App + Backend Step generation. This phase adds new tools that allow the AI to create and manage Custom Steps directly from the App designer context.

**Estimated Effort:** 2-3 days

---

## Goal

Provide the AI with tools to:
1. Create new Custom Steps from the App context
2. Save code to newly created steps
3. Define input/output variables for steps
4. Link steps to the current App
5. Execute and test step code

---

## Components to Create/Modify

### 1. Create `UnifiedAppMcpClient.cs`

**Location:** `src/Automaton.Studio/Services/Mcp/Clients/UnifiedAppMcpClient.cs`

This extends the existing `AppMcpClient` with backend step capabilities:

```csharp
using Automaton.Studio.Models;
using Automaton.Studio.Services;
using Automaton.Studio.Services.Mcp.EditorBridges;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// MCP client that supports both frontend (App) and backend (Step) operations
/// </summary>
public class UnifiedAppMcpClient : IUnifiedAppMcpClient
{
    private readonly IAppEditorBridge appEditorBridge;
    private readonly CustomStepsService customStepsService;
    private readonly AppStepsService appStepsService;
    private readonly AppsService appsService;
    private readonly ILogger<UnifiedAppMcpClient> logger;

    // Current context
    private AppModel? currentApp;
    private Dictionary<string, CustomStepContext> createdSteps = new();

    public UnifiedAppMcpClient(
        IAppEditorBridge appEditorBridge,
        CustomStepsService customStepsService,
        AppStepsService appStepsService,
        AppsService appsService,
        ILogger<UnifiedAppMcpClient> logger)
    {
        this.appEditorBridge = appEditorBridge;
        this.customStepsService = customStepsService;
        this.appStepsService = appStepsService;
        this.appsService = appsService;
        this.logger = logger;
    }

    public void SetCurrentApp(AppModel app)
    {
        currentApp = app;
        createdSteps.Clear();
    }

    public IEnumerable<Tool> GetTools()
    {
        return new List<Tool>
        {
            // Frontend tools (existing)
            CreateTool("get_current_code", "Get the current HTML/CSS/JavaScript code from the app editor"),
            CreateTool("save_app_code", "Save HTML/CSS/JavaScript code to the app", 
                new { code = "string: The complete HTML code to save" }),
            
            // Backend tools (NEW)
            CreateTool("create_backend_step", "Create a new Custom Step for backend logic",
                new { 
                    name = "string: Step name in PascalCase (e.g., SendEmailStep)", 
                    language = "string: Programming language (Python or Batch)",
                    description = "string: Optional description of what the step does"
                }),
            CreateTool("get_step_code", "Get the current code from a backend step",
                new { step_name = "string: Name of the step to read" }),
            CreateTool("save_step_code", "Save code to a backend step",
                new { 
                    step_name = "string: Name of the step to save to",
                    code = "string: The Python/Batch code to save"
                }),
            CreateTool("suggest_step_variables", "Define input and output variables for a step",
                new {
                    step_name = "string: Name of the step",
                    input_variables = "array: List of {name, type, description, is_sensitive} objects",
                    output_variables = "array: List of {name, type, description} objects"
                }),
            CreateTool("execute_step_code", "Test execute a backend step",
                new { 
                    step_name = "string: Name of the step to execute",
                    inputs = "object: Input variable values as key-value pairs"
                }),
            CreateTool("link_step_to_app", "Link a backend step to the current app",
                new { step_name = "string: Name of the step to link" }),
            CreateTool("get_linked_steps", "Get list of steps already linked to this app")
        };
    }

    public async Task<ToolResponse> HandleToolCall(string toolName, Dictionary<string, object> arguments)
    {
        try
        {
            logger.LogInformation("Handling tool call: {ToolName}", toolName);

            return toolName switch
            {
                // Frontend tools
                "get_current_code" => await HandleGetCurrentCode(),
                "save_app_code" => await HandleSaveAppCode(arguments),
                
                // Backend tools
                "create_backend_step" => await HandleCreateBackendStep(arguments),
                "get_step_code" => await HandleGetStepCode(arguments),
                "save_step_code" => await HandleSaveStepCode(arguments),
                "suggest_step_variables" => await HandleSuggestStepVariables(arguments),
                "execute_step_code" => await HandleExecuteStepCode(arguments),
                "link_step_to_app" => await HandleLinkStepToApp(arguments),
                "get_linked_steps" => await HandleGetLinkedSteps(),
                
                _ => CreateErrorResponse($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling tool call: {ToolName}", toolName);
            return CreateErrorResponse($"Error: {ex.Message}");
        }
    }

    #region Frontend Tool Handlers

    private async Task<ToolResponse> HandleGetCurrentCode()
    {
        var code = await appEditorBridge.GetCurrentCodeAsync();
        return CreateSuccessResponse(code ?? "");
    }

    private async Task<ToolResponse> HandleSaveAppCode(Dictionary<string, object> arguments)
    {
        var code = GetStringArgument(arguments, "code");
        if (string.IsNullOrEmpty(code))
        {
            return CreateErrorResponse("Code argument is required");
        }

        await appEditorBridge.SaveCodeAsync(code);
        return CreateSuccessResponse("App code saved successfully");
    }

    #endregion

    #region Backend Tool Handlers

    private async Task<ToolResponse> HandleCreateBackendStep(Dictionary<string, object> arguments)
    {
        var name = GetStringArgument(arguments, "name");
        var language = GetStringArgument(arguments, "language", "Python");
        var description = GetStringArgument(arguments, "description", "");

        if (string.IsNullOrEmpty(name))
        {
            return CreateErrorResponse("Step name is required");
        }

        // Validate language
        if (!new[] { "Python", "Batch", "PowerShell" }.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            return CreateErrorResponse("Language must be Python, Batch, or PowerShell");
        }

        try
        {
            // Create the step via service
            var newStep = new NewCustomStep { Name = name };
            var stepId = await customStepsService.Create(newStep);

            // Update with additional info
            var step = customStepsService.Get(stepId);
            step.DisplayName = name;
            step.Description = description;
            step.Definition.Language = language;
            step.VisibleInExplorer = true;
            step.Category = "My Steps";
            
            await customStepsService.Update(stepId, step);

            // Track the created step for this session
            createdSteps[name] = new CustomStepContext
            {
                StepId = stepId,
                Name = name,
                Language = language
            };

            logger.LogInformation("Created backend step: {StepName} ({Language})", name, language);

            return CreateSuccessResponse($"Created backend step '{name}' (ID: {stepId})");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create backend step: {StepName}", name);
            return CreateErrorResponse($"Failed to create step: {ex.Message}");
        }
    }

    private async Task<ToolResponse> HandleGetStepCode(Dictionary<string, object> arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        
        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return CreateErrorResponse($"Step '{stepName}' not found");
        }

        var code = step.Definition?.Code ?? "";
        return CreateSuccessResponse(code);
    }

    private async Task<ToolResponse> HandleSaveStepCode(Dictionary<string, object> arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        var code = GetStringArgument(arguments, "code");

        if (string.IsNullOrEmpty(code))
        {
            return CreateErrorResponse("Code argument is required");
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return CreateErrorResponse($"Step '{stepName}' not found");
        }

        step.Definition.Code = code;
        step.Definition.Version++;
        await customStepsService.Update(step.Id, step);

        logger.LogInformation("Saved code to step: {StepName} (v{Version})", stepName, step.Definition.Version);

        return CreateSuccessResponse($"Code saved to step '{stepName}' (version {step.Definition.Version})");
    }

    private async Task<ToolResponse> HandleSuggestStepVariables(Dictionary<string, object> arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        
        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return CreateErrorResponse($"Step '{stepName}' not found");
        }

        // Parse input variables
        if (arguments.TryGetValue("input_variables", out var inputVarsObj))
        {
            var inputVars = ParseVariableList(inputVarsObj);
            step.Definition.CodeInputVariables = inputVars.Select(v => new Automaton.Core.Models.StepVariable
            {
                Name = v.Name,
                Type = MapVariableType(v.Type),
                Description = v.Description,
                IsSensitive = v.IsSensitive
            }).ToList();
        }

        // Parse output variables
        if (arguments.TryGetValue("output_variables", out var outputVarsObj))
        {
            var outputVars = ParseVariableList(outputVarsObj);
            step.Definition.CodeOutputVariables = outputVars.Select(v => new Automaton.Core.Models.StepVariable
            {
                Name = v.Name,
                Type = MapVariableType(v.Type),
                Description = v.Description
            }).ToList();
        }

        await customStepsService.Update(step.Id, step);

        var inputCount = step.Definition.CodeInputVariables?.Count ?? 0;
        var outputCount = step.Definition.CodeOutputVariables?.Count ?? 0;

        logger.LogInformation("Updated variables for step: {StepName} ({InputCount} inputs, {OutputCount} outputs)", 
            stepName, inputCount, outputCount);

        return CreateSuccessResponse($"Updated step '{stepName}' with {inputCount} input(s) and {outputCount} output(s)");
    }

    private async Task<ToolResponse> HandleExecuteStepCode(Dictionary<string, object> arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        
        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return CreateErrorResponse($"Step '{stepName}' not found");
        }

        // Parse inputs
        var inputs = new Dictionary<string, string>();
        if (arguments.TryGetValue("inputs", out var inputsObj) && inputsObj is JsonElement inputsJson)
        {
            foreach (var prop in inputsJson.EnumerateObject())
            {
                inputs[prop.Name] = prop.Value.ToString();
            }
        }

        // Set input values on step
        if (step.Definition.CodeInputVariables != null)
        {
            foreach (var inputVar in step.Definition.CodeInputVariables)
            {
                if (inputs.TryGetValue(inputVar.Name, out var value))
                {
                    inputVar.Value = value;
                }
            }
        }

        try
        {
            // Get available runners
            // Note: For testing in AI context, we'd need runner selection logic
            // For now, return info about step readiness
            return CreateSuccessResponse($"""
                Step '{stepName}' is ready for execution.
                - Language: {step.Definition.Language}
                - Code length: {step.Definition.Code?.Length ?? 0} chars
                - Inputs: {step.Definition.CodeInputVariables?.Count ?? 0}
                - Outputs: {step.Definition.CodeOutputVariables?.Count ?? 0}
                
                Note: Actual execution requires a connected runner. 
                The step will be executed when the app calls automatonApi.executeStep('{stepName}', ...).
                """);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Execution check failed: {ex.Message}");
        }
    }

    private async Task<ToolResponse> HandleLinkStepToApp(Dictionary<string, object> arguments)
    {
        var stepName = GetStringArgument(arguments, "step_name");
        
        if (currentApp == null)
        {
            return CreateErrorResponse("No app context available");
        }

        var step = await ResolveStepAsync(stepName);
        if (step == null)
        {
            return CreateErrorResponse($"Step '{stepName}' not found");
        }

        try
        {
            await appStepsService.LinkStepAsync(currentApp.Id, step.Id);
            logger.LogInformation("Linked step '{StepName}' to app '{AppName}'", stepName, currentApp.Name);
            return CreateSuccessResponse($"Step '{stepName}' linked to app '{currentApp.Name}'");
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to link step: {ex.Message}");
        }
    }

    private async Task<ToolResponse> HandleGetLinkedSteps()
    {
        if (currentApp == null)
        {
            return CreateErrorResponse("No app context available");
        }

        try
        {
            var linkedSteps = await appStepsService.GetLinkedStepsAsync(currentApp.Id);
            
            if (!linkedSteps.Any())
            {
                return CreateSuccessResponse("No steps linked to this app yet.");
            }

            var stepsInfo = linkedSteps.Select(s => 
                $"- {s.DisplayName} ({s.Language}): {s.InputCount} inputs, {s.OutputCount} outputs");

            return CreateSuccessResponse($"""
                Linked steps:
                {string.Join("\n", stepsInfo)}
                """);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Failed to get linked steps: {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods

    private async Task<CustomStep?> ResolveStepAsync(string stepName)
    {
        // First check session-created steps
        if (createdSteps.TryGetValue(stepName, out var context))
        {
            return customStepsService.Get(context.StepId);
        }

        // Then search by name
        var allSteps = customStepsService.List(null);
        return allSteps.FirstOrDefault(s => 
            s.Name.Equals(stepName, StringComparison.OrdinalIgnoreCase) ||
            s.DisplayName.Equals(stepName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetStringArgument(Dictionary<string, object> arguments, string key, string defaultValue = "")
    {
        if (arguments.TryGetValue(key, out var value))
        {
            if (value is string s) return s;
            if (value is JsonElement je) return je.GetString() ?? defaultValue;
            return value?.ToString() ?? defaultValue;
        }
        return defaultValue;
    }

    private List<VariableDefinition> ParseVariableList(object? obj)
    {
        var result = new List<VariableDefinition>();
        
        if (obj is JsonElement jsonArray && jsonArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsonArray.EnumerateArray())
            {
                result.Add(new VariableDefinition
                {
                    Name = item.GetProperty("name").GetString() ?? "",
                    Type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "String" : "String",
                    Description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    IsSensitive = item.TryGetProperty("is_sensitive", out var s) && s.GetBoolean()
                });
            }
        }

        return result;
    }

    private Automaton.Core.Enums.VariableType MapVariableType(string type)
    {
        return type?.ToLower() switch
        {
            "string" => Automaton.Core.Enums.VariableType.Text,
            "text" => Automaton.Core.Enums.VariableType.Text,
            "number" => Automaton.Core.Enums.VariableType.Number,
            "int" => Automaton.Core.Enums.VariableType.Number,
            "integer" => Automaton.Core.Enums.VariableType.Number,
            "boolean" => Automaton.Core.Enums.VariableType.Boolean,
            "bool" => Automaton.Core.Enums.VariableType.Boolean,
            "json" => Automaton.Core.Enums.VariableType.Json,
            "object" => Automaton.Core.Enums.VariableType.Json,
            _ => Automaton.Core.Enums.VariableType.Text
        };
    }

    private Tool CreateTool(string name, string description, object? inputSchema = null)
    {
        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema != null 
                ? JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(inputSchema))
                : default
        };
    }

    private ToolResponse CreateSuccessResponse(string content)
    {
        return new ToolResponse
        {
            Content = new List<Content>
            {
                new TextContent { Text = content }
            }
        };
    }

    private ToolResponse CreateErrorResponse(string error)
    {
        return new ToolResponse
        {
            IsError = true,
            Content = new List<Content>
            {
                new TextContent { Text = error }
            }
        };
    }

    #endregion

    #region Inner Classes

    private class CustomStepContext
    {
        public Guid StepId { get; set; }
        public string Name { get; set; } = "";
        public string Language { get; set; } = "Python";
    }

    private class VariableDefinition
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "String";
        public string Description { get; set; } = "";
        public bool IsSensitive { get; set; }
    }

    #endregion
}

/// <summary>
/// Interface for unified app MCP client
/// </summary>
public interface IUnifiedAppMcpClient
{
    void SetCurrentApp(AppModel app);
    IEnumerable<Tool> GetTools();
    Task<ToolResponse> HandleToolCall(string toolName, Dictionary<string, object> arguments);
}
```

---

### 2. Create Interface Types for MCP

**Location:** `src/Automaton.Studio/Services/Mcp/Models/McpTypes.cs`

```csharp
namespace Automaton.Studio.Services.Mcp.Models;

/// <summary>
/// MCP Tool definition
/// </summary>
public class Tool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public System.Text.Json.JsonElement InputSchema { get; set; }
}

/// <summary>
/// MCP Tool response
/// </summary>
public class ToolResponse
{
    public bool IsError { get; set; }
    public List<Content> Content { get; set; } = new();
}

/// <summary>
/// Base content type
/// </summary>
public abstract class Content
{
    public string Type { get; set; } = "";
}

/// <summary>
/// Text content
/// </summary>
public class TextContent : Content
{
    public TextContent() { Type = "text"; }
    public string Text { get; set; } = "";
}
```

---

### 3. Update `AppPage.razor.cs` to Use Unified MCP Client

```csharp
// Add injection
[Inject] private IUnifiedAppMcpClient UnifiedMcpClient { get; set; }

// Update OnInitializedAsync
protected override async Task OnInitializedAsync()
{
    // ... existing code ...

    // Set up unified MCP client with app context
    UnifiedMcpClient.SetCurrentApp(currentApp);
}

// Update CreateAiChatContext to use unified client
private IAiChatContext CreateAiChatContext()
{
    return new UnifiedAppAiChatContext(
        UnifiedSystemPromptBuilder,
        AppId,
        GetCurrentCodeForAI,
        GetLinkedStepsForAI
    );
}
```

---

### 4. Register Services in DI

**Location:** `src/Automaton.Studio/Extensions/ServiceCollectionExtensions.cs`

```csharp
// Add MCP services
services.AddScoped<IUnifiedAppMcpClient, UnifiedAppMcpClient>();
```

---

### 5. Server-Side: `AppStepsService.cs`

**Location:** `src/Automaton.Studio.Server/Services/AppStepsService.cs`

```csharp
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing App-Step relationships
/// </summary>
public class AppStepsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly CustomStepsService customStepsService;
    private readonly UserContextService userContextService;
    private readonly ILogger logger;
    private readonly Guid userId;

    public AppStepsService(
        ApplicationDbContext dataContext,
        CustomStepsService customStepsService,
        UserContextService userContextService)
    {
        this.dataContext = dataContext;
        this.customStepsService = customStepsService;
        this.userContextService = userContextService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<AppStepsService>();
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        var links = await dataContext.AppCustomSteps
            .Where(acs => acs.AppId == appId)
            .ToListAsync();

        var result = new List<LinkedStepInfo>();

        foreach (var link in links)
        {
            var step = customStepsService.Get(link.CustomStepId);
            if (step != null)
            {
                result.Add(MapToLinkedStepInfo(step));
            }
        }

        return result;
    }

    /// <summary>
    /// Get available steps that can be linked (not already linked)
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetAvailableStepsAsync(Guid appId)
    {
        var linkedIds = await dataContext.AppCustomSteps
            .Where(acs => acs.AppId == appId)
            .Select(acs => acs.CustomStepId)
            .ToListAsync();

        var allSteps = customStepsService.List(null);

        return allSteps
            .Where(s => !linkedIds.Contains(s.Id))
            .Select(MapToLinkedStepInfo)
            .ToList();
    }

    /// <summary>
    /// Link a step to an app
    /// </summary>
    public async Task LinkStepAsync(Guid appId, Guid stepId)
    {
        // Verify ownership
        var app = await dataContext.Apps
            .Where(a => a.Id == appId && a.AppUsers.Any(au => au.UserId == userId))
            .FirstOrDefaultAsync();

        if (app == null)
        {
            throw new UnauthorizedAccessException("App not found or access denied");
        }

        var step = customStepsService.Get(stepId);
        if (step == null)
        {
            throw new InvalidOperationException("Step not found");
        }

        // Check if already linked
        var existingLink = await dataContext.AppCustomSteps
            .FirstOrDefaultAsync(acs => acs.AppId == appId && acs.CustomStepId == stepId);

        if (existingLink != null)
        {
            logger.Information("Step {StepId} already linked to app {AppId}", stepId, appId);
            return;
        }

        // Create link
        var link = new Entities.AppCustomStep
        {
            AppId = appId,
            CustomStepId = stepId,
            LinkedAt = DateTime.UtcNow,
            LinkedByUserId = userId
        };

        dataContext.AppCustomSteps.Add(link);
        await dataContext.SaveChangesAsync();

        logger.Information("Linked step {StepId} to app {AppId}", stepId, appId);
    }

    /// <summary>
    /// Unlink a step from an app
    /// </summary>
    public async Task UnlinkStepAsync(Guid appId, Guid stepId)
    {
        var link = await dataContext.AppCustomSteps
            .FirstOrDefaultAsync(acs => acs.AppId == appId && acs.CustomStepId == stepId);

        if (link == null)
        {
            return;
        }

        dataContext.AppCustomSteps.Remove(link);
        await dataContext.SaveChangesAsync();

        logger.Information("Unlinked step {StepId} from app {AppId}", stepId, appId);
    }

    private LinkedStepInfo MapToLinkedStepInfo(CustomStep step)
    {
        return new LinkedStepInfo
        {
            Id = step.Id,
            Name = step.Name,
            DisplayName = step.DisplayName ?? step.Name,
            Description = step.Description,
            Language = step.Definition?.Language ?? "Python",
            Version = step.Definition?.Version ?? 1,
            Created = step.Created,
            Updated = step.Updated,
            InputCount = step.Definition?.CodeInputVariables?.Count ?? 0,
            OutputCount = step.Definition?.CodeOutputVariables?.Count ?? 0,
            InputVariables = step.Definition?.CodeInputVariables?
                .Select(v => new StepVariableInfo
                {
                    Name = v.Name,
                    Description = v.Description,
                    Type = v.Type.ToString()
                }).ToList() ?? new(),
            OutputVariables = step.Definition?.CodeOutputVariables?
                .Select(v => new StepVariableInfo
                {
                    Name = v.Name,
                    Description = v.Description,
                    Type = v.Type.ToString()
                }).ToList() ?? new()
        };
    }
}
```

---

## Tool Usage Examples

### AI Creating a Backend Step

```
AI: I'll create a backend step for sending emails.

[Tool Call: create_backend_step]
{
  "name": "SendEmailStep",
  "language": "Python",
  "description": "Sends emails using SendGrid API"
}
? Response: "Created backend step 'SendEmailStep' (ID: abc123)"

[Tool Call: suggest_step_variables]
{
  "step_name": "SendEmailStep",
  "input_variables": [
    {"name": "api_key", "type": "string", "description": "SendGrid API key", "is_sensitive": true},
    {"name": "to_email", "type": "string", "description": "Recipient email address"},
    {"name": "subject", "type": "string", "description": "Email subject"},
    {"name": "body", "type": "string", "description": "Email body content"}
  ],
  "output_variables": [
    {"name": "success", "type": "boolean", "description": "Whether email was sent"},
    {"name": "message_id", "type": "string", "description": "SendGrid message ID"},
    {"name": "error_message", "type": "string", "description": "Error message if failed"}
  ]
}
? Response: "Updated step 'SendEmailStep' with 4 input(s) and 3 output(s)"

[Tool Call: save_step_code]
{
  "step_name": "SendEmailStep",
  "code": "import requests\n\nsuccess = False\nmessage_id = ''\nerror_message = ''\n\ntry:\n    # SendGrid API call\n    ...\nexcept Exception as e:\n    error_message = str(e)"
}
? Response: "Code saved to step 'SendEmailStep' (version 1)"

[Tool Call: link_step_to_app]
{
  "step_name": "SendEmailStep"
}
? Response: "Step 'SendEmailStep' linked to app 'ContactForm'"
```

---

## Testing Checklist

- [ ] `create_backend_step` creates a new Custom Step
- [ ] `get_step_code` retrieves existing step code
- [ ] `save_step_code` updates step code and increments version
- [ ] `suggest_step_variables` correctly sets input/output variables
- [ ] Sensitive variables are marked correctly
- [ ] `link_step_to_app` creates the App-Step relationship
- [ ] `get_linked_steps` returns linked steps
- [ ] Error handling works for invalid step names
- [ ] Multiple steps can be created in one session
- [ ] Steps are visible in Backend Actions tab after creation

---

## Next Steps

After Phase 4 is complete:
1. Proceed to [Phase 5: Testing & Integration](./05-phase5-testing.md)
2. End-to-end testing of the complete workflow
