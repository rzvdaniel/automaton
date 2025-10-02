# MCP Architecture Specifications (Clean Implementation - Frontend Direct)

## Project: Automaton.Mcp.Client

### 1. Overview

This document specifies the implementation of a standalone Model Context Protocol (MCP) client using the **official ModelContextProtocol C# SDK** and **Microsoft.Extensions.AI**. This implementation will enable iterative conversational flows with AI models directly in the **Automaton.Studio frontend** for real-time conversation display.

**Frontend-First Architecture**: The **Automaton.Studio frontend directly uses the MCP client** instead of communicating through the server, enabling real-time conversation updates in StepAiChatPanel.razor.

**Based on Official SDK Samples**: Uses actual patterns from `QuickstartClient`, `QuickstartWeatherServer`, `ChatWithTools`, and `EverythingServer`.

### 2. Key Principles

- ? **Frontend Direct**: Automaton.Studio frontend directly uses IAutomatonMcpClient
- ? **Official MCP SDK Only**: Use `ModelContextProtocol` NuGet package with official patterns
- ? **Microsoft.Extensions.AI**: Use standardized `IChatClient` interface exclusively
- ? **Real-time UI**: Full conversation flow visible in StepAiChatPanel.razor
- ? **No Server AI Logic**: Remove AiCodeGeneration from Automaton.Studio.Server entirely
- ? **Tool Support**: MCP tools using official SDK patterns only

### 3. Architecture Overview

```
???????????????????????????????????????????????????????????????
?                 Automaton.Studio (Frontend)                ?
?  ???????????????????????    ??????????????????????????????? ?
?  ?  StepAiChatPanel    ?    ?     Step Designer UI        ? ?
?  ?      .razor         ??????                             ? ?
?  ?                     ?    ?                             ? ?
?  ? Real-time updates   ?    ?                             ? ?
?  ? Conversation flow   ?    ?                             ? ?
?  ? Tool executions     ?    ?                             ? ?
?  ???????????????????????    ??????????????????????????????? ?
?              ?                                              ?
?              ?                                              ?
?  ??????????????????????????????????????????????????????????? ?
?  ?           IAutomatonMcpClient                           ? ?
?  ?                                                         ? ?
?  ?  • Microsoft.Extensions.AI IChatClient                 ? ?
?  ?  • Official MCP SDK                                    ? ?
?  ?  • In-memory MCP Server/Client                         ? ?
?  ?  • Real-time events                                    ? ?
?  ??????????????????????????????????????????????????????????? ?
?              ?                                              ?
?              ?                                              ?
?  ??????????????????????????????????????????????????????????? ?
?  ?                 MCP Tools                               ? ?
?  ?                                                         ? ?
?  ?  • get_custom_step_code  ??????????????????????????????????? Automaton.Studio.Server
?  ?  • execute_custom_step   ??????????????????????????????????? CustomStepExecuteService
?  ??????????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????????
                              ?
                              ?
                    ???????????????????????
                    ? OpenAI / Anthropic  ?
                    ?   (via official     ?
                    ?    SDK packages)    ?
                    ???????????????????????
```

### 4. Project Structure

#### 4.1 New Project: `Automaton.Mcp.Client`
- **Target Framework**: .NET 8
- **Project Type**: Class Library
- **Location**: `src/Automaton.Mcp.Client/Automaton.Mcp.Client.csproj`
- **Usage**: **Referenced by Automaton.Studio frontend only**

#### 4.2 Dependencies (Official SDK Only)
```xml
<PackageReference Include="ModelContextProtocol" Version="[latest-prerelease]" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
<PackageReference Include="System.IO.Pipelines" Version="8.0.0" />

<!-- Official AI Integration -->
<PackageReference Include="Microsoft.Extensions.AI" Version="[latest]" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="[latest]" />
<PackageReference Include="Anthropic.SDK" Version="[latest]" />

<!-- Optional: Telemetry -->
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="[latest]" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="[latest]" />
```

### 5. Frontend Integration Architecture

#### 5.1 Automaton.Studio Project References
```xml
<!-- Automaton.Studio.csproj -->
<ProjectReference Include="..\Automaton.Mcp.Client\Automaton.Mcp.Client.csproj" />
```

#### 5.2 Frontend Service Registration
```csharp
// Program.cs in Automaton.Studio
builder.Services.AddAutomatonMcpClient(builder.Configuration);

// Register HTTP client for server communication (for tools only)
builder.Services.AddHttpClient<IAutomatonServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ServerUrl"] ?? "https://localhost:7001");
});
```

#### 5.3 StepAiChatPanel Integration
```razor
@* StepAiChatPanel.razor *@
@implements IDisposable
@inject IAutomatonMcpClient McpClient
@inject IJSRuntime JSRuntime

<div class="ai-chat-panel">
    <!-- Real-time conversation display -->
    <div class="conversation-history">
        @foreach (var message in ConversationMessages)
        {
            <div class="message @GetMessageClass(message.Role)">
                <div class="message-content">
                    @((MarkupString)FormatMessageContent(message.Content))
                </div>
                <div class="message-timestamp">
                    @message.Timestamp.ToString("HH:mm:ss")
                </div>
            </div>
        }
    </div>

    <!-- Real-time tool execution display -->
    @if (ToolExecutions.Any())
    {
        <div class="tool-executions">
            <h4>?? Tool Executions</h4>
            @foreach (var execution in ToolExecutions)
            {
                <div class="tool-execution @(execution.IsSuccess ? "success" : "error")">
                    <div class="tool-header">
                        <strong>@execution.ToolName</strong>
                        <span class="status">@(execution.IsSuccess ? "?" : "?")</span>
                        <span class="timestamp">@execution.Timestamp.ToString("HH:mm:ss")</span>
                    </div>
                    <div class="tool-result">
                        <pre>@execution.Result</pre>
                    </div>
                </div>
            }
        </div>
    }

    <!-- User input -->
    <div class="input-area">
        <div class="input-group">
            <textarea @bind="CurrentMessage" 
                     @onkeypress="HandleKeyPress" 
                     placeholder="Describe what you want the AI to do..."
                     rows="3"
                     disabled="@IsGenerating"></textarea>
            <button @onclick="StartConversation" 
                   disabled="@(IsGenerating || string.IsNullOrWhiteSpace(CurrentMessage))"
                   class="btn btn-primary">
                @if (IsGenerating)
                {
                    <span class="spinner"></span>
                    <span>Generating...</span>
                }
                else
                {
                    <span>Generate</span>
                }
            </button>
        </div>
    </div>
</div>

@code {
    [Parameter] public string? CurrentStepId { get; set; }
    [Parameter] public string Language { get; set; } = "Python";
    [Parameter] public List<VariableInfo>? InputVariables { get; set; }
    [Parameter] public List<VariableInfo>? OutputVariables { get; set; }
    [Parameter] public EventCallback<string> OnCodeGenerated { get; set; }

    private string? _conversationId;
    private List<ConversationMessage> ConversationMessages = new();
    private List<ToolExecutionDisplay> ToolExecutions = new();
    private string CurrentMessage = string.Empty;
    private bool IsGenerating = false;

    protected override void OnInitialized()
    {
        // Subscribe to real-time conversation updates
        McpClient.ConversationUpdated += OnConversationUpdated;
    }

    private void OnConversationUpdated(object? sender, ConversationUpdatedEvent e)
    {
        if (e.ConversationId == _conversationId)
        {
            InvokeAsync(() =>
            {
                // Update conversation messages
                ConversationMessages = e.Messages;
                
                // Update tool executions with display info
                ToolExecutions = e.ToolResults.Select(tr => new ToolExecutionDisplay
                {
                    ToolName = e.ToolCalls.FirstOrDefault(tc => tc.Id == tr.ToolCallId)?.Name ?? "Unknown",
                    Result = tr.Result,
                    IsSuccess = tr.IsSuccess,
                    ErrorMessage = tr.ErrorMessage,
                    Timestamp = DateTime.Now
                }).ToList();

                StateHasChanged();
                
                // Auto-scroll to bottom
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100); // Let UI update
                    await JSRuntime.InvokeVoidAsync("scrollToBottom", ".conversation-history");
                });
            });
        }
    }

    private async Task StartConversation()
    {
        if (string.IsNullOrWhiteSpace(CurrentMessage) || IsGenerating)
            return;

        IsGenerating = true;
        var message = CurrentMessage;
        CurrentMessage = string.Empty;

        try
        {
            var request = new ConversationRequest
            {
                InitialMessage = message,
                SystemPrompt = BuildSystemPrompt(),
                Provider = GetSelectedAiProvider(), // From user preferences
                ApiKey = await GetApiKey(), // From user's API keys
                Model = GetSelectedModel(), // From user preferences
                ConversationHistory = ConversationMessages,
                Context = new Dictionary<string, object>
                {
                    ["userId"] = await GetCurrentUserId(),
                    ["currentStepId"] = CurrentStepId ?? string.Empty,
                    ["language"] = Language,
                    ["inputVariables"] = InputVariables ?? new List<VariableInfo>(),
                    ["outputVariables"] = OutputVariables ?? new List<VariableInfo>()
                }
            };

            var result = await McpClient.StartConversationAsync(request);
            _conversationId = result.ConversationId;

            if (result.IsSuccess && !string.IsNullOrEmpty(result.GeneratedCode))
            {
                // Notify parent component with generated code
                await OnCodeGenerated.InvokeAsync(result.GeneratedCode);
            }
        }
        catch (Exception ex)
        {
            // Handle error
            ConversationMessages.Add(new ConversationMessage("error", $"Error: {ex.Message}"));
            StateHasChanged();
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private string BuildSystemPrompt()
    {
        // Use the same prompt building logic from current AiStepGenerationService
        return $"""
            You are an expert automation developer. Generate {Language} code for a custom step in an automation workflow.

            IMPORTANT WORKFLOW INSTRUCTIONS:
            When tools are available, follow this process:
            1. ALWAYS start by using get_custom_step_code to retrieve the current code from the step
            2. Analyze the user's request and the current code
            3. Generate or modify the code as needed
            4. Use execute_custom_step to test the code and check if it works correctly
            5. If the execution shows errors or issues, modify the code and test again
            6. Only stop when the code executes successfully or when you're confident it meets the requirements
            7. When finished, provide the final working code as your response

            Requirements:
            - Generate clean, robust code with proper error handling
            - Use the specified language: {Language}
            - The code should be suitable for automation workflows
            - Follow best practices for the chosen language
            """;
    }

    // Helper methods to get user preferences, API keys, etc.
    private async Task<AiProvider> GetSelectedAiProvider() { /* Implementation */ }
    private async Task<string> GetApiKey() { /* Implementation */ }
    private async Task<string?> GetSelectedModel() { /* Implementation */ }
    private async Task<Guid> GetCurrentUserId() { /* Implementation */ }

    public void Dispose()
    {
        McpClient.ConversationUpdated -= OnConversationUpdated;
        if (_conversationId != null)
        {
            _ = Task.Run(() => McpClient.EndConversationAsync(_conversationId));
        }
    }

    private class ToolExecutionDisplay
    {
        public string ToolName { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
```

### 6. MCP Client Implementation (Frontend-Optimized)

#### 6.1 Server Communication for Tools
```csharp
public interface IAutomatonServerClient
{
    Task<CustomStepResponse> GetCustomStepAsync(Guid stepId, Guid userId);
    Task<ExecutionResult> ExecuteCustomStepAsync(Guid stepId, string code, Guid userId);
}

public class AutomatonServerClient : IAutomatonServerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AutomatonServerClient> _logger;

    public AutomatonServerClient(HttpClient httpClient, ILogger<AutomatonServerClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CustomStepResponse> GetCustomStepAsync(Guid stepId, Guid userId)
    {
        var response = await _httpClient.GetAsync($"/api/custom-steps/{stepId}?userId={userId}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CustomStepResponse>(json) ?? new CustomStepResponse();
    }

    public async Task<ExecutionResult> ExecuteCustomStepAsync(Guid stepId, string code, Guid userId)
    {
        var request = new ExecuteStepRequest
        {
            StepId = stepId,
            Code = code,
            UserId = userId
        };

        var response = await _httpClient.PostAsJsonAsync("/api/custom-steps/execute", request);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ExecutionResult>(json) ?? new ExecutionResult();
    }
}
```

#### 6.2 Updated MCP Tools (Server Communication)
```csharp
private McpServerTool CreateGetCustomStepCodeTool()
{
    return McpServerTool.Create(
        async (string stepId) =>
        {
            var serverClient = _serviceProvider.GetService<IAutomatonServerClient>();
            if (serverClient == null)
            {
                return "Error: Server client not available";
            }

            var userId = GetUserIdFromCurrentContext();
            var customStep = await serverClient.GetCustomStepAsync(Guid.Parse(stepId), userId);
            
            var result = new
            {
                code = customStep.Code ?? string.Empty,
                language = customStep.Language ?? "Python",
                stepId = stepId
            };

            return JsonSerializer.Serialize(result);
        },
        new McpServerToolDefinition
        {
            Name = "get_custom_step_code",
            Description = "Retrieves the current code from a custom step",
            InputSchema = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "stepId": {
                        "type": "string",
                        "description": "The ID of the custom step to retrieve code from"
                    }
                },
                "required": ["stepId"]
            }
            """)
        });
}

private McpServerTool CreateExecuteCustomStepTool()
{
    return McpServerTool.Create(
        async (string stepId, string code) =>
        {
            var serverClient = _serviceProvider.GetService<IAutomatonServerClient>();
            if (serverClient == null)
            {
                return "Error: Server client not available";
            }

            var userId = GetUserIdFromCurrentContext();
            var executionResult = await serverClient.ExecuteCustomStepAsync(Guid.Parse(stepId), code, userId);
            
            var result = new
            {
                success = executionResult.IsSuccess,
                logs = executionResult.Logs,
                output = executionResult.Output,
                error = executionResult.ErrorMessage
            };

            return JsonSerializer.Serialize(result);
        },
        new McpServerToolDefinition
        {
            Name = "execute_custom_step",
            Description = "Executes a custom step and returns the execution logs",
            InputSchema = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "stepId": {
                        "type": "string",
                        "description": "The ID of the custom step to execute"
                    },
                    "code": {
                        "type": "string",
                        "description": "The code to execute"
                    }
                },
                "required": ["stepId", "code"]
            }
            """)
        });
}
```

### 7. Server Updates (Minimal)

#### 7.1 Remove AiCodeGeneration from Server
```
src/Automaton.Studio.Server/
??? Services/
?   ??? AiCodeGeneration/          # ? DELETE ENTIRE FOLDER
?   ?   ??? AiStepGenerationService.cs
?   ??? AiProviders/               # ? DELETE ENTIRE FOLDER
?       ??? IAiProvider.cs
?       ??? OpenAiProvider.cs
?       ??? AnthropicProvider.cs
?       ??? AiProviderFactory.cs
??? Controllers/
    ??? CustomStepsController.cs   # ? KEEP - Used by MCP tools
```

#### 7.2 Server API Endpoints (For MCP Tools)
```csharp
[ApiController]
[Route("api/custom-steps")]
public class CustomStepsController : ControllerBase
{
    private readonly ICustomStepsService _customStepsService;
    private readonly ICustomStepExecuteService _executeService;

    [HttpGet("{stepId}")]
    public async Task<IActionResult> GetCustomStep(Guid stepId, [FromQuery] Guid userId)
    {
        var customStep = await _customStepsService.GetAsync(stepId, userId);
        if (customStep == null)
        {
            return NotFound();
        }

        return Ok(new CustomStepResponse
        {
            Code = customStep.Definition?.Code ?? string.Empty,
            Language = customStep.Definition?.Language ?? "Python"
        });
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteCustomStep([FromBody] ExecuteStepRequest request)
    {
        var result = await _executeService.ExecuteAsync(
            request.StepId.ToString(), 
            request.Code, 
            request.UserId, 
            CancellationToken.None);

        return Ok(new ExecutionResult
        {
            IsSuccess = result.IsSuccess,
            Logs = result.Logs,
            Output = result.Output,
            ErrorMessage = result.ErrorMessage
        });
    }
}

public class CustomStepResponse
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

public class ExecuteStepRequest
{
    public Guid StepId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

public class ExecutionResult
{
    public bool IsSuccess { get; set; }
    public string Logs { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
```

### 8. Updated File Structure

```
src/
??? Automaton.Mcp.Client/                    # NEW PROJECT
?   ??? Automaton.Mcp.Client.csproj          # Official SDK packages only
?   ??? IAutomatonMcpClient.cs
?   ??? AutomatonMcpClient.cs
?   ??? IAutomatonServerClient.cs            # NEW: Server communication
?   ??? AutomatonServerClient.cs             # NEW: HTTP client for tools
?   ??? Models/
?   ?   ??? ConversationRequest.cs
?   ?   ??? ConversationResult.cs
?   ?   ??? ConversationMessage.cs
?   ?   ??? Events.cs
?   ??? AiProviders/
?   ?   ??? IAutomatonAiClientFactory.cs
?   ?   ??? AutomatonAiClientFactory.cs
?   ??? Extensions/
?       ??? ServiceCollectionExtensions.cs
??? Automaton.Studio/                        # FRONTEND
?   ??? Automaton.Studio.csproj              # Add Automaton.Mcp.Client reference
?   ??? Program.cs                           # Register MCP services
?   ??? Pages/StepDesigner/Components/
?       ??? StepAiChatPanel.razor            # Real-time conversation UI
??? Automaton.Studio.Server/                 # SERVER (MINIMAL)
    ??? Controllers/
    ?   ??? CustomStepsController.cs          # API for MCP tools only
    ??? Services/
        ??? CustomStepsService.cs             # Keep existing
        ??? CustomStepExecuteService.cs       # Keep existing
```

### 9. Migration Benefits

#### 9.1 Frontend-First Architecture
- ? **Real-time UI**: Full conversation flow visible in StepAiChatPanel.razor
- ? **No Server Roundtrips**: Direct AI communication from frontend
- ? **Better UX**: Users see AI's thought process and tool executions live
- ? **Responsive**: No waiting for server processing

#### 9.2 Clean Server Architecture
- ? **Simplified Server**: Removes all AI generation logic from server
- ? **API-Only**: Server provides data APIs for MCP tools only
- ? **Better Separation**: Frontend handles AI, server handles data

#### 9.3 Official SDK Benefits
- ? **Standardization**: Uses Microsoft.Extensions.AI exclusively
- ? **Future-proof**: Built on official ecosystem
- ? **Tool Integration**: MCP tools work seamlessly
- ? **No Technical Debt**: Clean implementation from day one

### 10. Migration Plan

#### 10.1 Phase 1: Create Frontend MCP Client (Week 1)
1. Create `Automaton.Mcp.Client` project
2. Implement `IAutomatonMcpClient` with in-memory transport
3. Implement `IAutomatonServerClient` for HTTP communication
4. Create MCP tools that call server APIs

#### 10.2 Phase 2: Frontend Integration (Week 2)
1. Add MCP client reference to Automaton.Studio
2. Update StepAiChatPanel.razor for real-time conversation
3. Register MCP services in frontend Program.cs
4. Test end-to-end conversation flow

#### 10.3 Phase 3: Server Cleanup (Week 3)
1. **DELETE** AiCodeGeneration folder from server
2. **DELETE** AiProviders folder from server
3. Create minimal CustomStepsController API
4. Test MCP tools communication

#### 10.4 Phase 4: Testing & Polish (Week 4)
1. Comprehensive testing of frontend MCP integration
2. UI/UX refinements for real-time conversation
3. Performance optimization
4. Documentation

### 11. Success Criteria

- ? **Frontend Direct**: Automaton.Studio directly uses IAutomatonMcpClient
- ? **Real-time UI**: Full conversation flow visible in StepAiChatPanel.razor
- ? **No Server AI**: Removed all AI generation logic from Automaton.Studio.Server
- ? **Official SDK**: Uses Microsoft.Extensions.AI and official MCP SDK exclusively
- ? **Tool Transparency**: Users see AI's iterative development process live
- ? **Clean Architecture**: Clear separation between frontend AI and server data APIs

This updated specification creates a **frontend-first architecture** where the AI conversation happens directly in the Automaton.Studio frontend, providing real-time visibility into the AI's development process while keeping the server focused on data APIs only.