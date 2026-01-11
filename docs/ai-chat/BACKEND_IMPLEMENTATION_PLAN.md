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

### Phase 5: Vue.js Integration (Complete Session Persistence) 🎨

**Goal:** Add chat history persistence to existing Vue SSE implementation

**Current Status:** ✅ **~60% Complete**
- ✅ SSE client exists (`ai-conversation-client.ts`)
- ✅ `useAiChat` composable with full streaming support
- ✅ Tool call and result visualization
- ✅ Generic `AiChat` component
- ✅ Feature wrappers (`AppAiAssistant`, `StepAiAssistant`, `FlowAiAssistant`)
- ❌ Missing: Session persistence (chat history database integration)

**What's Left:** Only session/history persistence layer

#### 5.1 Add Chat History Service

```typescript
// src/automaton-studio-vue/src/shared/services/api/ai-chat-history.service.ts

import axiosInstance from './axios-instance'
import type { 
  ChatSession, 
  ChatMessage, 
  CreateChatSessionRequest, 
  SaveChatMessageRequest 
} from '@/shared/types/ai.types'

export const aiChatHistoryService = {
  /**
   * Get all sessions for a resource
   */
  async getSessions(resourceType: string, resourceId: string): Promise<ChatSession[]> {
    const response = await axiosInstance.get('/api/AiChatHistory/sessions', {
      params: { resourceType, resourceId }
    })
    return response.data
  },

  /**
   * Get messages for a session
   */
  async getSessionMessages(sessionId: string): Promise<ChatMessage[]> {
    const response = await axiosInstance.get(`/api/AiChatHistory/sessions/${sessionId}/messages`)
    return response.data
  },

  /**
   * Get or create active session for resource
   */
  async getOrCreateActiveSession(
    resourceType: string,
    resourceId: string
  ): Promise<ChatSession> {
    const response = await axiosInstance.get('/api/AiChatHistory/sessions/active', {
      params: { resourceType, resourceId }
    })
    return response.data
  },

  /**
   * Create new session
   */
  async createSession(request: CreateChatSessionRequest): Promise<ChatSession> {
    const response = await axiosInstance.post('/api/AiChatHistory/sessions', request)
    return response.data
  },

  /**
   * Activate a session
   */
  async activateSession(sessionId: string): Promise<void> {
    await axiosInstance.put(`/api/AiChatHistory/sessions/${sessionId}/activate`)
  },

  /**
   * Update session title
   */
  async updateSessionTitle(sessionId: string, title: string): Promise<void> {
    await axiosInstance.put(`/api/AiChatHistory/sessions/${sessionId}/title`, { title })
  },

  /**
   * Delete session
   */
  async deleteSession(sessionId: string): Promise<void> {
    await axiosInstance.delete(`/api/AiChatHistory/sessions/${sessionId}`)
  },

  /**
   * Save a message (for manual persistence if needed)
   */
  async saveMessage(request: SaveChatMessageRequest): Promise<void> {
    await axiosInstance.post('/api/AiChatHistory/messages', request)
  },

  /**
   * Clear session messages
   */
  async clearSessionMessages(sessionId: string): Promise<void> {
    await axiosInstance.delete(`/api/AiChatHistory/sessions/${sessionId}/messages`)
  }
}
```

#### 5.2 Enhance Existing `useAiChat` with Session Management

```typescript
// src/automaton-studio-vue/src/shared/composables/useAiChat.ts
// ADD these new methods to the existing composable:

import { aiChatHistoryService } from '../services/api/ai-chat-history.service'

export function useAiChat(resourceType: string, resourceId: string) {
  // ... existing state ...
  const sessions = ref<ChatSession[]>([])
  const activeSession = ref<ChatSession | null>(null)
  const isLoadingSessions = ref(false)

  /**
   * Load all sessions for this resource
   */
  async function loadSessions() {
    isLoadingSessions.value = true
    try {
      sessions.value = await aiChatHistoryService.getSessions(resourceType, resourceId)
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to load sessions'
      console.error('Error loading sessions:', err)
    } finally {
      isLoadingSessions.value = false
    }
  }

  /**
   * Get or create active session
   */
  async function getOrCreateActiveSession() {
    try {
      activeSession.value = await aiChatHistoryService.getOrCreateActiveSession(
        resourceType,
        resourceId
      )
      // Load messages from server
      await loadSessionMessages(activeSession.value.id)
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to get active session'
      console.error('Error getting active session:', err)
    }
  }

  /**
   * Load messages for a session
   */
  async function loadSessionMessages(sessionId: string) {
    try {
      const serverMessages = await aiChatHistoryService.getSessionMessages(sessionId)
      // Replace in-memory messages with server messages
      messages.value = serverMessages.map(m => ({
        ...m,
        timestamp: new Date(m.timestamp)
      }))
    } catch (err) {
      console.error('Error loading messages:', err)
    }
  }

  /**
   * Create a new session
   */
  async function createSession(title?: string) {
    try {
      const session = await aiChatHistoryService.createSession({
        resourceType,
        resourceId,
        title: title || `Chat ${new Date().toLocaleString()}`
      })
      sessions.value.push(session)
      await activateSession(session.id)
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to create session'
      console.error('Error creating session:', err)
    }
  }

  /**
   * Activate a different session
   */
  async function activateSession(sessionId: string) {
    try {
      await aiChatHistoryService.activateSession(sessionId)
      activeSession.value = sessions.value.find(s => s.id === sessionId) || null
      if (activeSession.value) {
        await loadSessionMessages(sessionId)
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to activate session'
      console.error('Error activating session:', err)
    }
  }

  /**
   * Delete a session
   */
  async function deleteSession(sessionId: string) {
    try {
      await aiChatHistoryService.deleteSession(sessionId)
      sessions.value = sessions.value.filter(s => s.id !== sessionId)
      
      // If deleted session was active, create new one
      if (activeSession.value?.id === sessionId) {
        await getOrCreateActiveSession()
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : 'Failed to delete session'
      console.error('Error deleting session:', err)
    }
  }

  // Update sendMessage to use session
  async function sendMessage(userMessage: string, systemPrompt?: string, context?: Record<string, any>) {
    // Ensure we have an active session
    if (!activeSession.value) {
      await getOrCreateActiveSession()
    }

    // ... rest of existing sendMessage code ...
    
    const request: StreamConversationRequest = {
      conversationId: conversationId.value || undefined,
      provider: selectedProvider.value,
      model: selectedModel.value || undefined,
      message: userMessage,
      systemPrompt,
      history: history.slice(0, -1),
      context: {
        resourceType,
        resourceId,
        ...context,
      },
      sessionId: activeSession.value?.id, // Add session ID
    }
    
    // ... continue with existing streaming code ...
  }

  return {
    // ... existing returns ...
    
    // NEW: Session management
    sessions,
    activeSession,
    isLoadingSessions,
    loadSessions,
    getOrCreateActiveSession,
    createSession,
    activateSession,
    deleteSession,
  }
}
```

#### 5.3 Add Session-Related Types to `ai.types.ts`

```typescript
// src/automaton-studio-vue/src/shared/types/ai.types.ts
// ADD these interfaces to the existing file:

export interface ChatSession {
  id: string
  resourceType: string
  resourceId: string
  title: string
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateChatSessionRequest {
  resourceType: string
  resourceId: string
  title: string
}

export interface SaveChatMessageRequest {
  sessionId: string
  role: string
  content: string
  messageType?: 'Text' | 'ToolExecution' | 'ToolResult'
  metadata?: string
}
```

#### 5.4 Update Feature Wrappers to Load Sessions

```typescript
// Example: src/automaton-studio-vue/src/features/apps/components/AppAiAssistant.vue
// MODIFY the existing component:

<script setup lang="ts">
import { onMounted } from 'vue'
import { useAiChat } from '@/shared/composables/useAiChat'

const props = defineProps<{
  appId: string
}>()

const {
  // ... existing destructured properties ...
  loadSessions,
  getOrCreateActiveSession,
} = useAiChat('WebApp', props.appId)

// Load sessions on mount
onMounted(async () => {
  await loadProviders()
  await getOrCreateActiveSession()  // This loads messages from DB
  await loadSessions()               // Load all sessions for switcher
})
</script>
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

1. **Vue.js Session Persistence (New Work):**
   - Load existing sessions on mount
   - Create new session
   - Activate different sessions
   - Messages persist across page reloads
   - Session switcher UI
   
2. **End-to-End Scenarios (Existing SSE + New Backend):**
   - **Step Editor:** Generate Python code → Execute via tool → View results → Save
   - **App Editor:** Request new feature → AI creates backend step → Updates frontend files
   - **Multi-turn Conversations:** Tool calls triggering more tool calls
   - **Session Persistence:** Reload page → Continue previous conversation from database

---

## Timeline

- **Week 1:** Phase 1 - Backend Tool Providers (StepMcpToolProvider, AppMcpToolProvider)
- **Week 2:** Phase 2 & 3 - Enhanced Controller & MCP Client Tool Integration
- **Week 3:** Phase 4 & 5 - DI Registration + Vue Session Persistence (chat history service)
- **Week 4:** Integration Testing & Bug Fixes

**Note:** Phase 5 is ~60% complete. Only need to add session persistence, not rebuild SSE client.

---

## Migration Notes

### Already Implemented ✅

- **Backend:** Chat history endpoints (full CRUD), Basic SSE streaming, API key management, Database schema
- **Frontend (Vue):** SSE client with POST support, `useAiChat` composable, Tool visualization, Generic `AiChat` component, Feature wrappers

### Net New Implementation 🚀

- **Backend:** Tool providers (server versions), Tool execution in MCP client, Context-aware system prompts, Auto-save during streaming
- **Frontend (Vue):** Chat history service integration, Session management in `useAiChat`, Session persistence types

### No Changes Needed ✅

- System prompt builders (already shared)
- AI providers (already portable)
- Database entities
- AiApiKeyService
- Vue SSE streaming infrastructure

---

## Next Steps - Complete Implementation Roadmap

### Phase 1: Backend Tool Providers (Week 1)

**Step 1.1: Create IMcpToolProvider Interface**
- File: `src/Automaton.Studio.Server/Services/Mcp/IMcpToolProvider.cs`
- Create interface with `GetAvailableTools()` and `ExecuteToolAsync()` methods
- Create supporting classes: `ToolDefinition`, `ToolResult`
- ✅ Exit Criteria: Interface compiles, no errors

**Step 1.2: Implement StepMcpToolProvider**
- File: `src/Automaton.Studio.Server/Services/Mcp/StepMcpToolProvider.cs`
- Port from: `src/Automaton.Studio/Services/Mcp/ToolProviders/StepMcpToolProvider.cs`
- Inject dependencies: `StepsService`, `IPythonExecutionService`, `IPowerShellExecutionService`
- Implement 4 tools:
  - `get_current_code` - Calls `StepsService.GetStepAsync()`
  - `execute_code` - Calls execution services based on language
  - `save_code` - Calls `StepsService.UpdateStepCodeAsync()`
  - `get_step_variables` - Returns step inputs/outputs as JSON
- ✅ Exit Criteria: All tools implemented, builds successfully

**Step 1.3: Implement AppMcpToolProvider**
- File: `src/Automaton.Studio.Server/Services/Mcp/AppMcpToolProvider.cs`
- Port from: `src/Automaton.Studio/Services/Mcp/ToolProviders/AppMcpToolProvider.cs`
- Inject dependencies: `AppsService`, `StepsService`
- Implement 6 tools:
  - `get_file_tree` - Calls `AppsService.GetFileTreeAsync()`
  - `read_file` - Calls `AppsService.ReadFileAsync()`
  - `write_file` - Calls `AppsService.WriteFileAsync()`
  - `delete_file` - Calls `AppsService.DeleteFileAsync()`
  - `create_backend_step` - Creates step + links to app
  - `list_backend_steps` - Calls `AppsService.GetLinkedStepsAsync()`
- ✅ Exit Criteria: All tools implemented, builds successfully

**Step 1.4: Unit Test Tool Providers**
- Create `StepMcpToolProviderTests.cs`
- Create `AppMcpToolProviderTests.cs`
- Mock all service dependencies
- Test each tool individually (success and error cases)
- ✅ Exit Criteria: All tests pass

---

### Phase 2: Enhanced AiConversationController (Week 2, Part 1)

**Step 2.1: Update StreamConversationRequest DTO**
- File: `src/Automaton.Studio.Server/Controllers/AiConversationController.cs`
- Add new properties: `SessionId`, `ResourceType`, `ResourceId`, `Language`
- ✅ Exit Criteria: DTO compiles, no breaking changes to existing API

**Step 2.2: Inject System Prompt Builders**
- File: `src/Automaton.Studio.Server/Controllers/AiConversationController.cs`
- Add constructor parameters:
  - `IStepSystemPromptBuilder`
  - `IAppSystemPromptBuilder`
- ✅ Exit Criteria: Controller builds with new dependencies

**Step 2.3: Inject Tool Providers**
- File: `src/Automaton.Studio.Server/Controllers/AiConversationController.cs`
- Add constructor parameters:
  - `StepMcpToolProvider`
  - `AppMcpToolProvider`
- ✅ Exit Criteria: Controller builds with tool providers

**Step 2.4: Implement BuildSystemPromptAsync() Method**
- Switch on `ResourceType` to select appropriate prompt builder
- Return context-aware system prompt
- ✅ Exit Criteria: Method compiles, returns correct prompts for Step/App

**Step 2.5: Implement GetToolProvider() Method**
- Switch on `ResourceType` to select appropriate tool provider
- Return `StepMcpToolProvider` or `AppMcpToolProvider`
- ✅ Exit Criteria: Method returns correct provider based on resource type

**Step 2.6: Implement SaveConversationUpdateAsync() Method**
- Save messages via `IAiChatHistoryService.SaveMessageAsync()`
- Save tool calls with metadata
- Handle errors gracefully (don't break streaming)
- ✅ Exit Criteria: Messages save to database during streaming

**Step 2.7: Enhance StreamConversation() Endpoint**
- Add calls to:
  - `BuildSystemPromptAsync()`
  - `GetToolProvider()`
  - `_mcpClient.SetToolProvider()`
  - `SaveConversationUpdateAsync()` in event handler
- Add `SessionId` to request context
- ✅ Exit Criteria: Endpoint compiles, uses tool providers

---

### Phase 3: AutomatonMcpClient Tool Integration (Week 2, Part 2)

**Step 3.1: Update IAutomatonMcpClient Interface**
- File: `src/Automaton.Mcp/IAutomatonMcpClient.cs`
- Add method: `void SetToolProvider(IMcpToolProvider? toolProvider)`
- ✅ Exit Criteria: Interface compiles

**Step 3.2: Port IMcpToolProvider to Automaton.Mcp**
- File: `src/Automaton.Mcp/IMcpToolProvider.cs`
- Copy interface from `Automaton.Studio.Server` (make it shared)
- OR: Add project reference from `Automaton.Mcp` → `Automaton.Studio.Server`
- ✅ Exit Criteria: Interface available in MCP project

**Step 3.3: Implement SetToolProvider() in AutomatonMcpClient**
- File: `src/Automaton.Mcp/AutomatonMcpClient.cs`
- Add private field: `IMcpToolProvider? _toolProvider`
- Implement setter method
- ✅ Exit Criteria: Can inject tool provider

**Step 3.4: Integrate Tool Execution in OpenAI Provider**
- File: `src/Automaton.Mcp/AutomatonMcpClient.cs` (or provider-specific file)
- When AI returns function call:
  - Extract tool name and arguments
  - Call `_toolProvider.ExecuteToolAsync()`
  - Send result back to AI
  - Raise `ConversationUpdated` event with tool call + result
- ✅ Exit Criteria: Tools execute during AI conversations

**Step 3.5: Integrate Tool Execution in Anthropic Provider**
- Same as Step 3.4 but for Anthropic's tool format
- ✅ Exit Criteria: Tools execute for Anthropic provider

**Step 3.6: Test Tool Execution Flow**
- Create integration test with mock tool provider
- Verify tool calls are executed
- Verify results are sent back to AI
- Verify events are raised
- ✅ Exit Criteria: Integration test passes

---

### Phase 4: DI Registration (Week 3, Part 1)

**Step 4.1: Register System Prompt Builders**
- File: `src/Automaton.Studio.Server/Program.cs`
- Add:
  ```csharp
  builder.Services.AddScoped<IStepSystemPromptBuilder, StepSystemPromptBuilder>();
  builder.Services.AddScoped<IAppSystemPromptBuilder, AppSystemPromptBuilder>();
  ```
- ✅ Exit Criteria: Application starts without DI errors

**Step 4.2: Register Tool Providers**
- File: `src/Automaton.Studio.Server/Program.cs`
- Add:
  ```csharp
  builder.Services.AddScoped<StepMcpToolProvider>();
  builder.Services.AddScoped<AppMcpToolProvider>();
  ```
- ✅ Exit Criteria: Application starts, providers can be injected

**Step 4.3: Verify AutomatonMcpClient Registration**
- Ensure `IAutomatonMcpClient` is registered as scoped
- ✅ Exit Criteria: Client available for injection

**Step 4.4: Test End-to-End DI**
- Start application
- Make request to `/api/ai/conversation/stream`
- Verify no DI errors
- ✅ Exit Criteria: Full dependency graph resolves

---

### Phase 5: Vue.js Session Persistence (Week 3, Part 2)

**Step 5.1: Create AI Chat History Service**
- File: `src/automaton-studio-vue/src/shared/services/api/ai-chat-history.service.ts`
- Implement methods calling existing backend API:
  - `getSessions()`
  - `getSessionMessages()`
  - `getOrCreateActiveSession()`
  - `createSession()`
  - `activateSession()`
  - `updateSessionTitle()`
  - `deleteSession()`
  - `saveMessage()`
  - `clearSessionMessages()`
- ✅ Exit Criteria: Service compiles, all methods implemented

**Step 5.2: Add Session Types to ai.types.ts**
- File: `src/automaton-studio-vue/src/shared/types/ai.types.ts`
- Add interfaces:
  - `ChatSession`
  - `CreateChatSessionRequest`
  - `SaveChatMessageRequest`
- ✅ Exit Criteria: Types compile, no TypeScript errors

**Step 5.3: Enhance useAiChat Composable**
- File: `src/automaton-studio-vue/src/shared/composables/useAiChat.ts`
- Add state: `sessions`, `activeSession`, `isLoadingSessions`
- Add methods:
  - `loadSessions()`
  - `getOrCreateActiveSession()`
  - `loadSessionMessages()`
  - `createSession()`
  - `activateSession()`
  - `deleteSession()`
- Update `sendMessage()` to include `sessionId` in request
- ✅ Exit Criteria: Composable compiles, exports new methods

**Step 5.4: Update Feature Wrappers**
- Files:
  - `src/automaton-studio-vue/src/features/apps/components/AppAiAssistant.vue`
  - `src/automaton-studio-vue/src/features/steps/components/StepAiAssistant.vue`
  - `src/automaton-studio-vue/src/features/flows/components/FlowAiAssistant.vue`
- Add `onMounted` hook to call:
  - `loadProviders()`
  - `getOrCreateActiveSession()`
  - `loadSessions()`
- ✅ Exit Criteria: Components load sessions on mount

**Step 5.5: Test Session Persistence**
- Open app/step editor
- Send message
- Reload page
- Verify conversation history loads
- ✅ Exit Criteria: Messages persist across page reloads

---

### Phase 6: End-to-End Testing & Validation (Week 4)

**Step 6.1: Manual Testing - Step Editor**
- Open custom step in Vue editor
- Ask AI to "generate a Python script that fetches weather data"
- Verify:
  - AI calls `get_current_code` tool
  - AI generates code
  - AI calls `execute_code` tool
  - Execution result shows in chat
  - AI calls `save_code` tool
  - Code is saved to database
- ✅ Exit Criteria: Full tool chain works for steps

**Step 6.2: Manual Testing - App Editor**
- Open web app in Vue editor
- Ask AI to "add a contact form to index.html"
- Verify:
  - AI calls `get_file_tree` tool
  - AI calls `read_file` for index.html
  - AI calls `write_file` with updated HTML
  - File is updated in database
  - Preview shows new form
- ✅ Exit Criteria: Full tool chain works for apps

**Step 6.3: Manual Testing - Multi-turn Conversation**
- Ask AI to "create a backend step for the contact form"
- Verify:
  - AI calls `create_backend_step` tool
  - New step is created and linked
  - AI can reference the step in subsequent messages
- ✅ Exit Criteria: Multi-turn tool conversations work

**Step 6.4: Manual Testing - Session Management**
- Create multiple chat sessions
- Switch between sessions
- Verify conversation history loads correctly
- Delete a session
- Verify it's removed from UI and database
- ✅ Exit Criteria: Session CRUD operations work

**Step 6.5: Performance Testing**
- Test with large conversation history (50+ messages)
- Verify streaming performance
- Verify tool execution doesn't block UI
- ✅ Exit Criteria: No performance regressions

**Step 6.6: Error Handling Testing**
- Test with invalid API key
- Test with network errors
- Test with tool execution failures
- Verify errors are displayed gracefully
- ✅ Exit Criteria: All error cases handled

**Step 6.7: Browser Compatibility**
- Test in Chrome, Firefox, Edge
- Verify SSE streaming works in all browsers
- ✅ Exit Criteria: Works in all major browsers

---

## Implementation Checklist Summary

### Backend (C#)
- [ ] Phase 1.1: Create `IMcpToolProvider` interface
- [ ] Phase 1.2: Implement `StepMcpToolProvider`
- [ ] Phase 1.3: Implement `AppMcpToolProvider`
- [ ] Phase 1.4: Unit test tool providers
- [ ] Phase 2.1: Update `StreamConversationRequest` DTO
- [ ] Phase 2.2-2.3: Inject prompt builders and tool providers
- [ ] Phase 2.4-2.5: Implement prompt/provider selection methods
- [ ] Phase 2.6: Implement auto-save logic
- [ ] Phase 2.7: Enhance streaming endpoint
- [ ] Phase 3.1-3.2: Update MCP client interface
- [ ] Phase 3.3-3.5: Integrate tool execution
- [ ] Phase 3.6: Integration testing
- [ ] Phase 4.1-4.4: DI registration and validation

### Frontend (Vue.js/TypeScript)
- [ ] Phase 5.1: Create `ai-chat-history.service.ts`
- [ ] Phase 5.2: Add session types
- [ ] Phase 5.3: Enhance `useAiChat` composable
- [ ] Phase 5.4: Update feature wrappers
- [ ] Phase 5.5: Test session persistence

### Testing & Validation
- [ ] Phase 6.1: Step editor end-to-end test
- [ ] Phase 6.2: App editor end-to-end test
- [ ] Phase 6.3: Multi-turn conversation test
- [ ] Phase 6.4: Session management test
- [ ] Phase 6.5: Performance testing
- [ ] Phase 6.6: Error handling validation
- [ ] Phase 6.7: Browser compatibility

---

## Dependencies Between Phases

- **Phase 2** depends on **Phase 1** (needs tool providers)
- **Phase 3** depends on **Phase 1** (needs `IMcpToolProvider` interface)
- **Phase 4** depends on **Phases 1-3** (registers all components)
- **Phase 5** is independent (can be done in parallel with Phase 1-3)
- **Phase 6** depends on **all previous phases**

## Recommended Implementation Order

1. Start with **Phase 1** (tool providers) - foundation for everything
2. Do **Phase 5** next (quick win, enables session persistence)
3. Then **Phase 2** (enhanced controller)
4. Then **Phase 3** (MCP client integration)
5. Then **Phase 4** (DI registration)
6. Finally **Phase 6** (testing and validation)