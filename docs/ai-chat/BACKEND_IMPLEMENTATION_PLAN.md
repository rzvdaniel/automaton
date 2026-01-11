# AI Chat Backend Implementation Plan

**Date:** January 11, 2026  
**Purpose:** Comprehensive plan to port AI chat functionality from Blazor to ASP.NET Core API for Vue.js frontend

---

## Current State Analysis

### ✅ Already Implemented (Backend)

1. **AiChatHistoryController** (`src/Automaton.Studio.Server/Controllers/AiChatHistoryController.cs`)
   - ✅ GET `/api/AiChatHistory/sessions` - List sessions for resource
   - ✅ GET `/api/AiChatHistory/sessions/{id}` - Get session details
   - ✅ GET `/api/AiChatHistory/sessions/{id}/messages` - Get session messages
   - ✅ GET `/api/AiChatHistory/sessions/{id}/full` - Get session with messages
   - ✅ GET `/api/AiChatHistory/sessions/active` - Get or create active session
   - ✅ POST `/api/AiChatHistory/sessions` - Create new session
   - ✅ PUT `/api/AiChatHistory/sessions/{id}/activate` - Activate session
   - ✅ PUT `/api/AiChatHistory/sessions/{id}/title` - Update session title
   - ✅ DELETE `/api/AiChatHistory/sessions/{id}` - Delete session
   - ✅ POST `/api/AiChatHistory/messages` - Save single message
   - ✅ POST `/api/AiChatHistory/messages/batch` - Save multiple messages
   - ✅ DELETE `/api/AiChatHistory/sessions/{id}/messages` - Clear session messages

2. **AiChatHistoryService** (`src/Automaton.Studio.Server/Services/AiChatHistoryService.cs`)
   - ✅ Full implementation with database operations
   - ✅ User authorization checks
   - ✅ Session and message CRUD operations

3. **AiConversationController** (`src/Automaton.Studio.Server/Controllers/AiConversationController.cs`)
   - ✅ Basic SSE streaming endpoint: `POST /api/ai/conversation/stream`
   - ✅ Secure API key retrieval (never exposes to client)
   - ✅ Integration with `IAutomatonMcpClient`
   - ⚠️ **Missing:** Context-aware prompts, tool provider injection, auto-save

4. **AutomatonMcpClient** (`src/Automaton.Mcp/AutomatonMcpClient.cs`)
   - ✅ OpenAI and Anthropic provider support
   - ✅ Basic streaming
   - ⚠️ **Missing:** Tool provider integration, tool execution

5. **AiApiKeyService** (`src/Automaton.Studio.Server/Services/AiApiKeyService.cs`)
   - ✅ Encrypted API key storage
   - ✅ Decryption for server-side use
   - ✅ Provider management

### 🔧 Exists in Blazor (Needs Backend Port)

1. **System Prompt Builders** (Shared Libraries - Already Available)
   - ✅ `StepSystemPromptBuilder.cs` (`src/Automaton.Studio/Services/Mcp/SystemPrompts/`)
   - ✅ `AppSystemPromptBuilder.cs` (`src/Automaton.Studio/Services/Mcp/SystemPrompts/`)
   - **Action:** Just register in API DI container

2. **MCP Tool Providers** (Blazor-Specific - Need Backend Versions)
## Decisions (Resolved)

- Blazor AI chat will be retired; no dual implementation is required during migration.
- Code execution will use the existing runner infrastructure (no new sandbox layer).
- AI provider quota management will be implemented later.
- Streaming stays with SSE (WebSocket not needed for this phase).

## Vue Integration Note

- The Vue app already has `AppAiAssistant.vue` using `<AiChat>`; reuse this component and point it to the new SSE endpoint rather than creating a new panel.
   - ✅ `OpenAiProvider.cs` (`src/Automaton.Studio/Services/Mcp/Clients/Providers/`)
   - ✅ `AnthropicProvider.cs` (`src/Automaton.Studio/Services/Mcp/Clients/Providers/`)
   - **Status:** Already .NET Standard, can be used server-side

4. **Conversation Logic** (Blazor-Specific - Needs Backend Port)
   - 🚀 `AiChatPanel.razor.cs` - Main conversation orchestration
   - 🚀 `ConversationResultProcessor.cs` - Process AI responses
   - 🚀 Result strategies (AgentMode, AskMode)
   - **Action:** Port orchestration logic to controller/service

---

## Implementation Plan

### Phase 1: Tool Providers (Backend Versions) 🚀

**Goal:** Create server-side tool providers that can execute tools securely

#### 1.1 Create IMcpToolProvider Interface (Server Version)

```csharp
// src/Automaton.Studio.Server/Services/Mcp/IMcpToolProvider.cs

namespace Automaton.Studio.Server.Services.Mcp;

public interface IMcpToolProvider
{
    /// <summary>
    /// Get available tools for this provider
    /// </summary>
    List<ToolDefinition> GetAvailableTools();
    
    /// <summary>
    /// Execute a tool with given arguments
    /// </summary>
    Task<ToolResult> ExecuteToolAsync(
        string toolName, 
        Dictionary<string, object> arguments, 
        Guid userId, 
        Dictionary<string, object> context,
        CancellationToken cancellationToken = default);
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> InputSchema { get; set; } = new();
}

public class ToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
```

#### 1.2 Implement StepMcpToolProvider (Backend)

**Port from:** `src/Automaton.Studio/Services/Mcp/ToolProviders/StepMcpToolProvider.cs`

**Tools to implement:**
- `get_current_code` - Get Step code from database
- `execute_code` - Execute Python/PowerShell/JavaScript via execution services
- `save_code` - Save code back to Step entity
- `get_step_variables` - Get Step input/output variables
- `suggest_variables` - AI-assisted variable suggestions

```csharp
// src/Automaton.Studio.Server/Services/Mcp/StepMcpToolProvider.cs

public class StepMcpToolProvider : IMcpToolProvider
{
    private readonly StepsService _stepsService;
    private readonly IPythonExecutionService _pythonExecutionService;
    private readonly IPowerShellExecutionService _powerShellExecutionService;
    private readonly ILogger<StepMcpToolProvider> _logger;
    
    public List<ToolDefinition> GetAvailableTools()
    {
        return new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "get_current_code",
                Description = "Get the current code of the step being edited",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["stepId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the step"
                        }
                    },
                    ["required"] = new[] { "stepId" }
                }
            },
            new ToolDefinition
            {
                Name = "execute_code",
                Description = "Execute code and return the results",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["code"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The code to execute"
                        },
                        ["language"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["enum"] = new[] { "Python", "PowerShell", "JavaScript", "Batch" }
                        },
                        ["inputs"] = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["description"] = "Input variables for the code"
                        }
                    },
                    ["required"] = new[] { "code", "language" }
                }
            },
            new ToolDefinition
            {
                Name = "save_code",
                Description = "Save the generated code to the step",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["stepId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the step"
                        },
                        ["code"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The code to save"
                        }
                    },
                    ["required"] = new[] { "stepId", "code" }
                }
            },
            new ToolDefinition
            {
                Name = "get_step_variables",
                Description = "Get the input and output variables defined for the step",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["stepId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the step"
                        }
                    },
                    ["required"] = new[] { "stepId" }
                }
            }
        };
    }
    
    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> arguments,
        Guid userId,
        Dictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolName switch
            {
                "get_current_code" => await GetCurrentCode(arguments, userId, cancellationToken),
                "execute_code" => await ExecuteCode(arguments, context, cancellationToken),
                "save_code" => await SaveCode(arguments, userId, cancellationToken),
                "get_step_variables" => await GetStepVariables(arguments, userId, cancellationToken),
                _ => new ToolResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Unknown tool: {toolName}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new ToolResult
            {
                IsSuccess = false,
                ErrorMessage = $"Error executing {toolName}: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolResult> GetCurrentCode(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stepId = Guid.Parse(arguments["stepId"].ToString()!);
        var step = await _stepsService.GetStepAsync(stepId, userId);
        
        if (step == null)
        {
            return new ToolResult
            {
                IsSuccess = false,
                ErrorMessage = "Step not found or access denied"
            };
        }
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = step.Code ?? string.Empty
        };
    }
    
    private async Task<ToolResult> ExecuteCode(
        Dictionary<string, object> arguments,
        Dictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        var code = arguments["code"].ToString()!;
        var language = arguments["language"].ToString()!;
        var inputs = arguments.ContainsKey("inputs")
            ? (Dictionary<string, object>)arguments["inputs"]
            : new Dictionary<string, object>();
        
        // Execute based on language
        ExecutionResult? result = language.ToLowerInvariant() switch
        {
            "python" => await _pythonExecutionService.ExecuteAsync(code, inputs, cancellationToken),
            "powershell" => await _powerShellExecutionService.ExecuteAsync(code, inputs, cancellationToken),
            _ => throw new NotSupportedException($"Language {language} not supported for execution")
        };
        
        if (result.Success)
        {
            return new ToolResult
            {
                IsSuccess = true,
                Result = $"Execution successful.\n\nOutput:\n{result.Output}\n\nVariables:\n{FormatVariables(result.Variables)}"
            };
        }
        else
        {
            return new ToolResult
            {
                IsSuccess = false,
                ErrorMessage = $"Execution failed: {result.Error}"
            };
        }
    }
    
    private async Task<ToolResult> SaveCode(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stepId = Guid.Parse(arguments["stepId"].ToString()!);
        var code = arguments["code"].ToString()!;
        
        await _stepsService.UpdateStepCodeAsync(stepId, code, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = "Code saved successfully"
        };
    }
    
    private async Task<ToolResult> GetStepVariables(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var stepId = Guid.Parse(arguments["stepId"].ToString()!);
        var step = await _stepsService.GetStepAsync(stepId, userId);
        
        if (step == null)
        {
            return new ToolResult
            {
                IsSuccess = false,
                ErrorMessage = "Step not found or access denied"
            };
        }
        
        var result = new
        {
            inputs = step.Inputs ?? new List<VariableDto>(),
            outputs = step.Outputs ?? new List<VariableDto>()
        };
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = JsonSerializer.Serialize(result)
        };
    }
    
    private string FormatVariables(Dictionary<string, object> variables)
    {
        return string.Join("\n", variables.Select(v => $"{v.Key}: {v.Value}"));
    }
}
```

#### 1.3 Implement AppMcpToolProvider (Backend)

**Port from:** `src/Automaton.Studio/Services/Mcp/ToolProviders/AppMcpToolProvider.cs`

**Tools to implement:**
- `get_file_tree` - Get app file structure
- `read_file` - Read app file content
- `write_file` - Write/update app file
- `delete_file` - Delete app file
- `create_backend_step` - Create and link backend step to app
- `list_backend_steps` - List linked backend steps
- `update_backend_step` - Update backend step code

```csharp
// src/Automaton.Studio.Server/Services/Mcp/AppMcpToolProvider.cs

public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly AppsService _appsService;
    private readonly StepsService _stepsService;
    private readonly ILogger<AppMcpToolProvider> _logger;
    
    public List<ToolDefinition> GetAvailableTools()
    {
        return new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "get_file_tree",
                Description = "Get the complete file tree of the web app",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["appId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the web app"
                        }
                    },
                    ["required"] = new[] { "appId" }
                }
            },
            new ToolDefinition
            {
                Name = "read_file",
                Description = "Read the content of a specific file in the web app",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["appId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the web app"
                        },
                        ["filePath"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Path to the file (e.g., 'index.html', 'js/app.js')"
                        }
                    },
                    ["required"] = new[] { "appId", "filePath" }
                }
            },
            new ToolDefinition
            {
                Name = "write_file",
                Description = "Write or update a file in the web app",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["appId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        },
                        ["filePath"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        },
                        ["content"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["required"] = new[] { "appId", "filePath", "content" }
                }
            },
            new ToolDefinition
            {
                Name = "create_backend_step",
                Description = "Create a new backend custom step and link it to the app",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["appId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        },
                        ["stepName"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        },
                        ["code"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        },
                        ["language"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["enum"] = new[] { "Python", "PowerShell" }
                        },
                        ["inputs"] = new Dictionary<string, object>
                        {
                            ["type"] = "array"
                        },
                        ["outputs"] = new Dictionary<string, object>
                        {
                            ["type"] = "array"
                        }
                    },
                    ["required"] = new[] { "appId", "stepName", "code", "language" }
                }
            },
            new ToolDefinition
            {
                Name = "list_backend_steps",
                Description = "List all backend steps linked to the app",
                InputSchema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["appId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string"
                        }
                    },
                    ["required"] = new[] { "appId" }
                }
            }
        };
    }
    
    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> arguments,
        Guid userId,
        Dictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolName switch
            {
                "get_file_tree" => await GetFileTree(arguments, userId, cancellationToken),
                "read_file" => await ReadFile(arguments, userId, cancellationToken),
                "write_file" => await WriteFile(arguments, userId, cancellationToken),
                "delete_file" => await DeleteFile(arguments, userId, cancellationToken),
                "create_backend_step" => await CreateBackendStep(arguments, userId, cancellationToken),
                "list_backend_steps" => await ListBackendSteps(arguments, userId, cancellationToken),
                _ => new ToolResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Unknown tool: {toolName}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new ToolResult
            {
                IsSuccess = false,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolResult> GetFileTree(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var fileTree = await _appsService.GetFileTreeAsync(appId, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = JsonSerializer.Serialize(fileTree)
        };
    }
    
    private async Task<ToolResult> ReadFile(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var filePath = arguments["filePath"].ToString()!;
        
        var content = await _appsService.ReadFileAsync(appId, filePath, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = content
        };
    }
    
    private async Task<ToolResult> WriteFile(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var filePath = arguments["filePath"].ToString()!;
        var content = arguments["content"].ToString()!;
        
        await _appsService.WriteFileAsync(appId, filePath, content, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = $"File {filePath} written successfully"
        };
    }
    
    private async Task<ToolResult> DeleteFile(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var filePath = arguments["filePath"].ToString()!;
        
        await _appsService.DeleteFileAsync(appId, filePath, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = $"File {filePath} deleted successfully"
        };
    }
    
    private async Task<ToolResult> CreateBackendStep(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var stepName = arguments["stepName"].ToString()!;
        var code = arguments["code"].ToString()!;
        var language = arguments["language"].ToString()!;
        
        // Parse inputs and outputs if provided
        var inputs = arguments.ContainsKey("inputs")
            ? JsonSerializer.Deserialize<List<VariableDto>>(arguments["inputs"].ToString()!)
            : new List<VariableDto>();
        var outputs = arguments.ContainsKey("outputs")
            ? JsonSerializer.Deserialize<List<VariableDto>>(arguments["outputs"].ToString()!)
            : new List<VariableDto>();
        
        // Create step
        var step = await _stepsService.CreateStepAsync(new CreateStepRequest
        {
            Name = stepName,
            Language = language,
            Code = code,
            Inputs = inputs,
            Outputs = outputs
        }, userId);
        
        // Link to app
        await _appsService.LinkStepToAppAsync(appId, step.Id, userId);
        
        var result = new
        {
            stepId = step.Id,
            stepName = step.Name,
            message = $"Backend step '{stepName}' created and linked successfully"
        };
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = JsonSerializer.Serialize(result)
        };
    }
    
    private async Task<ToolResult> ListBackendSteps(
        Dictionary<string, object> arguments,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var appId = Guid.Parse(arguments["appId"].ToString()!);
        var steps = await _appsService.GetLinkedStepsAsync(appId, userId);
        
        return new ToolResult
        {
            IsSuccess = true,
            Result = JsonSerializer.Serialize(steps)
        };
    }
}
```

---

### Phase 2: Enhanced AiConversationController 🚀

**Goal:** Add context-aware system prompts, tool execution, and auto-save

#### 2.1 Update StreamConversationRequest

```csharp
// src/Automaton.Studio.Server/Controllers/AiConversationController.cs

public class StreamConversationRequest
{
    public string? ConversationId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public List<ConversationMessageDto>? History { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    
    // Context-specific fields
    public Guid? SessionId { get; set; }
    public string? ResourceType { get; set; } // "CustomStep", "WebApp"
    public Guid? ResourceId { get; set; }
    public string? Language { get; set; } // For Steps
}
```

#### 2.2 Enhance Controller

```csharp
[HttpPost("stream")]
public async Task StreamConversation(
    [FromBody] StreamConversationRequest request,
    CancellationToken cancellationToken)
{
    // ... SSE headers ...
    
    try
    {
        var userId = GetUserId();
        
        // 1. Get API key (secure - server-only)
        var apiKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(
            userId, request.Provider);
        
        if (string.IsNullOrEmpty(apiKey))
        {
            await WriteErrorEvent("No API key configured");
            return;
        }
        
        // 2. Build context-aware system prompt
        var systemPrompt = await BuildSystemPromptAsync(request, userId);
        
        // 3. Load conversation history
        var history = await LoadConversationHistoryAsync(request.SessionId, userId);
        
        // 4. Get tool provider based on resource type
        var toolProvider = GetToolProvider(request.ResourceType);
        
        // 5. Setup AutomatonMcpClient with tool provider
        _mcpClient.SetToolProvider(toolProvider);
        
        // 6. Subscribe to conversation updates
        _mcpClient.ConversationUpdated += async (sender, e) =>
        {
            await WriteConversationEvent(e);
            
            // Auto-save messages during streaming
            if (request.SessionId.HasValue)
            {
                await SaveConversationUpdateAsync(e, request.SessionId.Value, userId);
            }
        };
        
        // 7. Build request
        var conversationRequest = new ConversationRequest
        {
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
            InitialMessage = request.Message,
            SystemPrompt = systemPrompt,
            Provider = request.Provider,
            Model = request.Model,
            ApiKey = apiKey,
            ConversationHistory = history,
            Context = BuildContextDictionary(request, userId)
        };
        
        // 8. Start streaming
        var result = await _mcpClient.StartConversationAsync(conversationRequest, cancellationToken);
        
        // 9. Write done event
        if (result.IsSuccess)
        {
            await WriteEvent("done", new
            {
                conversationId = result.ConversationId,
                success = true,
                generatedCode = result.GeneratedCode
            });
        }
        else
        {
            await WriteErrorEvent(result.ErrorMessage ?? "Failed");
        }
    }
    catch (OperationCanceledException)
    {
        await WriteEvent("cancelled", new { message = "Cancelled" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during AI conversation");
        await WriteErrorEvent($"Error: {ex.Message}");
    }
}

private async Task<string> BuildSystemPromptAsync(StreamConversationRequest request, Guid userId)
{
    // Client can override
    if (!string.IsNullOrEmpty(request.SystemPrompt))
        return request.SystemPrompt;
    
    // Build context-aware prompt
    return request.ResourceType?.ToLowerInvariant() switch
    {
        "customstep" => _stepPromptBuilder.BuildSystemPrompt(request.Language ?? "Python"),
        "webapp" => _appPromptBuilder.BuildSystemPrompt(),
        _ => "You are a helpful AI assistant for the Automaton platform."
    };
}

private IMcpToolProvider? GetToolProvider(string? resourceType)
{
    return resourceType?.ToLowerInvariant() switch
    {
        "customstep" => _stepToolProvider,
        "webapp" => _appToolProvider,
        _ => null
    };
}

private Dictionary<string, object> BuildContextDictionary(StreamConversationRequest request, Guid userId)
{
    var context = request.Context ?? new Dictionary<string, object>();
    
    // Add essential context
    if (request.ResourceType != null)
        context["resourceType"] = request.ResourceType;
    if (request.ResourceId.HasValue)
        context["resourceId"] = request.ResourceId.Value;
    if (request.Language != null)
        context["language"] = request.Language;
    
    context["userId"] = userId;
    
    return context;
}

private async Task SaveConversationUpdateAsync(
    ConversationUpdatedEvent update,
    Guid sessionId,
    Guid userId)
{
    try
    {
        // Save messages
        foreach (var message in update.Messages)
        {
            await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = message.Role,
                Content = message.Content,
                MessageType = ChatMessageType.Text
            }, userId);
        }
        
        // Save tool calls/results as special messages
        foreach (var toolCall in update.ToolCalls)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                toolCallId = toolCall.Id,
                toolName = toolCall.Name,
                arguments = toolCall.Arguments
            });
            
            await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = "tool",
                Content = $"Executing {toolCall.Name}",
                MessageType = ChatMessageType.ToolExecution,
                Metadata = metadata
            }, userId);
        }
        
        foreach (var toolResult in update.ToolResults)
        {
            // Update metadata of existing tool call message
            // (Implementation details...)
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error saving conversation update");
        // Don't throw - history saving shouldn't break streaming
    }
}
```

---

### Phase 3: AutomatonMcpClient Enhancement 🚀

**Goal:** Integrate tool providers for dynamic tool execution

#### 3.1 Update IAutomatonMcpClient

```csharp
// src/Automaton.Mcp/IAutomatonMcpClient.cs

public interface IAutomatonMcpClient
{
    Task<ConversationResult> StartConversationAsync(
        ConversationRequest request, 
        CancellationToken cancellationToken = default);
    
    Task<ConversationResult> ContinueConversationAsync(
        string conversationId, 
        string message, 
        CancellationToken cancellationToken = default);
    
    Task<bool> EndConversationAsync(
        string conversationId, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Set the tool provider for executing MCP tools
    /// </summary>
    void SetToolProvider(IMcpToolProvider? toolProvider);
    
    event EventHandler<ConversationUpdatedEvent> ConversationUpdated;
}
```

#### 3.2 Implement Tool Execution in AutomatonMcpClient

```csharp
// src/Automaton.Mcp/AutomatonMcpClient.cs

public class AutomatonMcpClient : IAutomatonMcpClient
{
    private IMcpToolProvider? _toolProvider;
    
    public void SetToolProvider(IMcpToolProvider? toolProvider)
    {
        _toolProvider = toolProvider;
    }
    
    public async Task<ConversationResult> StartConversationAsync(
        ConversationRequest request,
        CancellationToken cancellationToken)
    {
        // Get tools from provider
        var tools = _toolProvider?.GetAvailableTools() ?? new List<ToolDefinition>();
        
        // Pass tools to AI provider (OpenAI/Anthropic)
        // When AI requests a tool:
        //   1. Extract tool call (name, arguments)
        //   2. Execute via _toolProvider.ExecuteToolAsync()
        //   3. Stream tool call + result via ConversationUpdated event
        //   4. Continue conversation with tool result
        
        // ... (implementation)
    }
    
    private async Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall,
        ConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (_toolProvider == null)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                IsSuccess = false,
                ErrorMessage = "No tool provider configured"
            };
        }
        
        // Extract userId from context
        var userId = request.Context?.ContainsKey("userId") == true
            ? (Guid)request.Context["userId"]
            : Guid.Empty;
        
        return await _toolProvider.ExecuteToolAsync(
            toolCall.Name,
            toolCall.Arguments,
            userId,
            request.Context ?? new Dictionary<string, object>(),
            cancellationToken
        );
    }
}
```

---

### Phase 4: Register Services in DI 🔧

```csharp
// src/Automaton.Studio.Server/Program.cs

// System Prompt Builders
builder.Services.AddScoped<IStepSystemPromptBuilder, StepSystemPromptBuilder>();
builder.Services.AddScoped<IAppSystemPromptBuilder, AppSystemPromptBuilder>();

// Tool Providers
builder.Services.AddScoped<StepMcpToolProvider>();
builder.Services.AddScoped<AppMcpToolProvider>();

// MCP Client
builder.Services.AddScoped<IAutomatonMcpClient, AutomatonMcpClient>();

// Already registered:
// - IAiChatHistoryService, AiChatHistoryService
// - AiApiKeyService
```

---

## Testing Strategy

### Unit Tests

1. **Tool Providers:**
   - Test each tool individually
   - Mock services (StepsService, AppsService)
   - Test error handling

2. **AiConversationController:**
   - Test system prompt building
   - Test tool provider selection
   - Test SSE event formatting

3. **AutomatonMcpClient:**
   - Test tool execution flow
   - Test streaming with tools
   - Test error recovery

### Integration Tests

1. **End-to-End AI Conversation:**
   - Real AI provider (OpenAI test key)
   - Execute tools (mock execution services)
   - Verify SSE stream format

2. **Chat History Persistence:**
   - Create session
   - Stream conversation
   - Verify auto-save
   - Load history

### Manual Testing

1. **Vue.js Frontend Integration:**
   - EventSource connection
   - Real-time message updates
   - Tool execution visualization
   - Session management UI

---

## Timeline

- **Week 1:** Phase 1 - Tool Providers
- **Week 2:** Phase 2 & 3 - Enhanced Controller & MCP Client
- **Week 3:** Phase 4 - DI Registration, Unit Tests
- **Week 4:** Integration Tests, Vue.js Integration

---

## Migration Notes

### Already Implemented ✅

- Chat history endpoints (full CRUD)
- Basic SSE streaming
- API key management
- Database schema

### Net New Implementation 🚀

- Tool providers (backend versions)
- Tool execution in MCP client
- Context-aware system prompts
- Auto-save during streaming

### No Changes Needed ✅

- System prompt builders (already shared)
- AI providers (already portable)
- Database entities
- AiApiKeyService

---

## Next Steps

1. Review this plan with team
2. Start Phase 1: Implement `StepMcpToolProvider`
3. Test tool execution with real Step
4. Continue with remaining phases

