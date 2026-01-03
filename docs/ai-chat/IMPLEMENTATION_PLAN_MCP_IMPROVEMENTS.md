# MCP Tool Provider Improvements Plan

## Problem Statement

1. **Missing Code Execution Testing**: UnifiedAppMcpToolProvider cannot test backend steps, causing AI to miss errors
2. **Inefficient Step Creation**: Creates steps without auto-linking, requiring separate manual linking
3. **Unused Code**: AppMcpToolProvider is registered but never used
4. **Code Duplication**: Potential duplication between StepMcpToolProvider and UnifiedAppMcpToolProvider

## Analysis Summary

### Current State

#### StepMcpToolProvider.cs ✅
- **Purpose**: Custom Step Designer (editing individual steps)
- **Context**: Works with current step editor via `IStepEditorBridge`
- **Tools**:
  - ✅ `execute_code` - Tests step execution
  - ✅ `get_current_code`, `save_code`, `get_step_variables`, `suggest_variables`
- **Execution**: Uses `ICustomStepExecuteService`

#### UnifiedAppMcpToolProvider.cs ⚠️
- **Purpose**: Full-stack app generation (frontend + backend)
- **Context**: Works with database entities via services
- **Tools**:
  - ✅ `create_backend_step`, `get_step_code`, `save_step_code`, `suggest_step_variables`
  - ✅ `get_current_code`, `save_app_code` (frontend)
  - ⚠️ `link_step_to_app` (inefficient - should auto-link on creation)
  - ❌ **Missing `execute_backend_step_code`** - Cannot test steps!
- **Issues**:
  - Creates steps via `CustomStepsService.Create()` instead of `AppStepsService.CreateStepForAppAsync()`
  - Requires separate linking step (error-prone)
  - No testing capability

#### AppMcpToolProvider.cs ❌
- **Status**: UNUSED (legacy code)
- **Evidence**: Registered in DI, injected in AppPage, but never called
- **Recommendation**: DELETE

### Key Discovery: AppStepsService Has Better API

**Current wrong approach in UnifiedAppMcpToolProvider:**
```csharp
// Step 1: Create step
var createdStep = await _customStepsService.Create(newStep);

// Step 2: Link to app (SEPARATE CALL, AI might forget!)
await _appStepsService.LinkStepAsync(appId, stepId);
```

**Correct approach (already exists!):**
```csharp
// Single atomic operation - create AND link
var linkedStep = await _appStepsService.CreateStepForAppAsync(appId, stepName);
```

## Implementation Plan

---

### Phase 1: Fix Step Creation to Auto-Link ✅ HIGH PRIORITY

**Goal**: Steps created in UnifiedAppMcpToolProvider are automatically linked to the app

#### Changes Required

**File: `UnifiedAppMcpToolProvider.cs`**

1. **Update `HandleCreateBackendStep` method (lines 391-454)**
   - Replace `_customStepsService.Create()` with `_appStepsService.CreateStepForAppAsync()`
   - Remove manual property updates (handled by service)
   - Update success message to indicate auto-linking

```csharp
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
        // Use AppStepsService to create AND link in one operation
        var linkedStep = await _appStepsService.CreateStepForAppAsync(_currentAppId.Value, name);

        // Load the full step to update language and description
        var createdStep = await _customStepsService.Load(linkedStep.Id);
        createdStep.Definition.Language = language;
        createdStep.Description = description;
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

        // Notify subscribers
        BackendStepsChanged?.Invoke(this, new BackendStepsChangedEventArgs
        {
            AppId = _currentAppId.Value,
            StepName = name,
            ChangeType = "created_and_linked" // Updated event type
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
```

2. **Update `create_backend_step` tool description**
   - Update description to mention automatic linking

```csharp
Description = "Create a new Custom Step for backend logic and automatically link it to the current app. The step will execute on a Runner."
```

3. **Repurpose `link_step_to_app` tool**
   - Update description to clarify it's for EXISTING steps only
   - Add validation to prevent linking steps that were just created in this session

```csharp
new McpTool
{
    Name = "link_existing_step_to_app",  // Renamed for clarity
    Description = "Link an EXISTING step (created outside this app context) to the current app. Note: Steps created via create_backend_step are automatically linked.",
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
}
```

4. **Update `HandleLinkStepToApp` to prevent redundant linking**

```csharp
private async Task<McpToolResult> HandleLinkStepToApp(JsonElement arguments)
{
    var stepName = GetStringArgument(arguments, "step_name");

    if (!_currentAppId.HasValue)
    {
        return new McpToolResult { Success = false, Error = "No app context available" };
    }

    // Check if this step was created in this session (already linked)
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
```

**Testing:**
- Create app via UI
- Use AI chat to create backend step
- Verify step appears in "App Steps" tab immediately without manual linking
- Verify `WebAppSteps` table has the link record
- Try to link the same step again - should get "already linked" message

---

### Phase 2: Add Code Execution Testing ✅ HIGH PRIORITY

**Goal**: AI can test backend steps before finalizing the app

#### Changes Required

**File: `UnifiedAppMcpToolProvider.cs`**

1. **Inject `ICustomStepExecuteService` in constructor**

```csharp
private readonly ICustomStepExecuteService _customStepExecuteService;

public UnifiedAppMcpToolProvider(
    ILogger<UnifiedAppMcpToolProvider> logger,
    IAppEditorBridge appEditorBridge,
    IStepEditorBridge stepEditorBridge,
    CustomStepsService customStepsService,
    AppStepsService appStepsService,
    AiCodeCleanupService codeCleanupService,
    ICustomStepExecuteService customStepExecuteService)  // ADD THIS
{
    _logger = logger;
    _appEditorBridge = appEditorBridge;
    _customStepsService = customStepsService;
    _appStepsService = appStepsService;
    _codeCleanupService = codeCleanupService;
    _customStepExecuteService = customStepExecuteService;  // ADD THIS
}
```

2. **Add new `execute_backend_step_code` tool to `GetToolsAsync()`**

```csharp
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
}
```

3. **Implement `HandleExecuteBackendStepCode` method**

```csharp
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

        // Build detailed result
        var result = new
        {
            success = executionResult.Success,
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
            Success = executionResult.Success,
            Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            Error = executionResult.Success ? null :
                (executionResult.ErrorInfo?.GetFormattedErrorForAI() ??
                 string.Join("\n", executionResult.Errors ?? Array.Empty<string>()))
        };
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
```

4. **Add tool to switch statement**

```csharp
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
            "execute_backend_step_code" => await HandleExecuteBackendStepCode(arguments),  // ADD THIS
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
```

**Testing:**
- Create app with AI
- Ask AI to create a backend step that makes an API call
- Verify AI automatically tests the step after creation
- Introduce an error (wrong API endpoint)
- Verify AI detects the error and fixes it

---

### Phase 3: Update System Prompts 📝 MEDIUM PRIORITY

**Goal**: Guide AI to follow best practices

#### Changes Required

**File: `UnifiedAppSystemPromptBuilder.cs`**

1. **Add workflow guidance**
   - Emphasize testing steps before finalizing
   - Show example workflow

```markdown
## Backend Step Development Workflow

When creating backend steps for the app:

1. **Create** the step with `create_backend_step` (automatically linked to app)
2. **Define variables** with `suggest_step_variables` if needed
3. **Write code** with `save_step_code`
4. **TEST IMMEDIATELY** with `execute_backend_step_code` using sample inputs
5. **Fix errors** if execution fails
6. **Re-test** until successful
7. **Only then** proceed to integrate with frontend

### Critical Testing Rule

ALWAYS test backend steps with `execute_backend_step_code` before:
- Linking them to frontend code
- Marking the app as complete
- Moving to the next step

### Example Workflow

```
AI: "I'll create a step to fetch weather data"
1. create_backend_step(name="FetchWeatherStep", language="python")
2. suggest_step_variables(
     input_variables=[{name: "city", description: "City name"}],
     output_variables=[{name: "temperature", description: "Current temp"}]
   )
3. save_step_code(step_name="FetchWeatherStep", code="...")
4. execute_backend_step_code(
     step_name="FetchWeatherStep",
     input_variables={"city": "London"}
   )
5. If SUCCESS → Integrate with frontend
   If FAILED → Fix code and re-test
```

## Available Tools - Execution Order

**Backend Step Tools (use in this order):**
1. `create_backend_step` - Creates step (auto-linked)
2. `suggest_step_variables` - Define inputs/outputs
3. `save_step_code` - Write the code
4. `execute_backend_step_code` - **TEST BEFORE PROCEEDING**
5. `get_step_code` - Review if needed
6. `get_linked_steps` - See all app steps

**Frontend Tools:**
1. `get_current_code` - Get current HTML/CSS/JS
2. `save_app_code` - Save frontend code

**Reusing Existing Steps:**
- `link_existing_step_to_app` - Link steps created outside this app
```

---

### Phase 4: Remove Unused AppMcpToolProvider 🗑️ LOW PRIORITY

**Goal**: Clean up legacy code

#### Files to Delete

1. `src/Automaton.Studio/Services/Mcp/ToolProviders/AppMcpToolProvider.cs`
2. `src/Automaton.Studio/Services/Mcp/Clients/AppMcpClient.cs`
3. `src/Automaton.Studio/Services/Mcp/Clients/IAppMcpClient.cs`

#### Files to Modify

**File: `ServiceCollectionExtensions.cs`**
- Remove line 103: `services.AddScoped<AppMcpToolProvider>();`
- Remove line 108: `services.AddScoped<IAppMcpClient, AppMcpClient>();`

**File: `AppPage.razor.cs`**
- Remove line 62: `[Inject] private IAppMcpClient WebAppMcpClient { get; set; }`

**Testing:**
- Build solution - should compile without errors
- Run app - should work normally
- Verify no references to AppMcpClient remain

---

### Phase 5: Documentation Updates 📚 LOW PRIORITY

**Goal**: Document the improvements

#### Update Files

1. **`CLAUDE.md`** - Add note about MCP tool improvements
2. **`docs/mcp/MCP_ARCHITECTURE_SPECIFICATIONS.md`** - Update tool listings
3. **Create `docs/mcp/BACKEND_STEP_TESTING.md`** - Document testing workflow

---

## Summary of Changes

### High Priority (Do First)
1. ✅ **Phase 1**: Auto-link steps on creation
   - Impact: Prevents AI forgetting to link steps
   - Effort: ~1 hour

2. ✅ **Phase 2**: Add execute tool
   - Impact: Solves core issue - AI can detect errors
   - Effort: ~2 hours

### Medium Priority
3. ✅ **Phase 3**: Update system prompts
   - Impact: Guides AI to test steps
   - Effort: ~1 hour

### Low Priority (Code Cleanup)
4. ⏳ **Phase 4**: Remove AppMcpToolProvider
   - Impact: Code cleanup
   - Effort: ~30 minutes

5. ⏳ **Phase 5**: Documentation
   - Impact: Future reference
   - Effort: ~1 hour

## Expected Outcomes

After implementation:
- ✅ AI automatically links steps when creating them
- ✅ AI can test backend steps and detect API errors
- ✅ Fewer broken apps due to untested backend code
- ✅ Cleaner codebase without unused providers
- ✅ Better AI guidance through updated prompts

## Risks & Mitigations

**Risk**: Existing apps might rely on manual linking
**Mitigation**: Auto-linking is backwards compatible - just makes it automatic

**Risk**: ICustomStepExecuteService might not work for all languages
**Mitigation**: Already tested in StepMcpToolProvider - same implementation

**Risk**: Breaking changes to tool schemas
**Mitigation**: Additive changes only - existing tools still work

## Testing Strategy

1. **Unit Tests**: Add tests for new execute handler
2. **Integration Tests**: Test full workflow (create → test → link)
3. **Manual Testing**: Create apps via AI chat and verify steps work
4. **Regression Testing**: Verify existing apps still work
