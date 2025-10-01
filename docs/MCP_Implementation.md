# MCP (Model Context Protocol) Implementation Guide

## Overview

This document describes the MCP (Model Context Protocol) implementation in Automaton, which provides AI models with structured access to application data and actions through tool calling.

## Architecture

### Core Components

1. **IMcpTool Interface** (`Services/Mcp/IMcpTool.cs`)
   - Defines the contract for all MCP tools
   - Properties: Name, Description, ParametersSchema
   - Method: ExecuteAsync(parameters, userId, cancellationToken)

2. **McpService** (`Services/Mcp/McpService.cs`)
   - Manages tool registration and execution
   - Provides tool definitions to AI providers
   - Handles tool execution with error handling

3. **Tool Models** (`Services/Mcp/McpToolCall.cs`)
   - `McpToolCall`: Represents a tool invocation from AI
   - `McpToolResult`: Contains tool execution results
   - `McpToolDefinition`: Tool definition for AI models

## Implemented Tools

### 1. GetCustomStepCodeTool

**Purpose:** Allows AI to read the current code and metadata of a custom step being edited.

**Parameters:**
```json
{
  "stepId": "guid-of-custom-step"
}
```

**Returns:**
```json
{
  "success": true,
  "stepId": "...",
  "stepName": "My Custom Step",
  "language": "Python",
  "code": "# Current code here...",
  "inputVariables": [
    {
      "name": "input1",
      "type": "String",
      "description": "Description"
    }
  ],
  "outputVariables": [...]
}
```

**Use Cases:**
- AI reads existing code before suggesting modifications
- AI understands context of input/output variables
- AI can analyze current implementation before debugging

### 2. ExecuteCustomStepTool

**Purpose:** Allows AI to execute a custom step and retrieve logs and outputs.

**Parameters:**
```json
{
  "stepId": "guid-of-custom-step",
  "inputValues": {
    "input1": "test value",
    "input2": 42
  }
}
```

**Returns:**
```json
{
  "success": true,
  "stepId": "...",
  "stepName": "My Custom Step",
  "logs": "INFO: Starting...\nINFO: Processing...\n",
  "outputs": {
    "result": "processed value",
    "status": "success"
  }
}
```

**Use Cases:**
- AI tests generated code automatically
- AI debugs failing code by reading execution logs
- AI validates fixes before presenting to user

## Integration with AI Providers

### OpenAI Provider

The OpenAI provider has been enhanced to support function calling:

```csharp
public async Task<string> GenerateCodeAsync(
    string apiKey, 
    string prompt, 
    string model, 
    List<ConversationMessage>? conversationHistory = null,
    List<McpToolDefinition>? tools = null)
```

**How it works:**
1. Tools are passed to OpenAI's API in the `tools` parameter
2. OpenAI decides whether to call tools based on conversation context
3. Tool calls are returned in the response
4. Tools are executed server-side
5. Results are returned to AI for further processing

### AI Step Generation Service

The service orchestrates tool usage:

1. Retrieves available tools from McpService
2. Passes tools to AI provider
3. Detects tool calls in AI response
4. Executes tools via McpService
5. Returns results to frontend

## Frontend Integration

### Chat Panel Display

Tool calls and results are displayed in the AI chat panel:

- **Tool Call Message** (MessageType.ToolCall)
  - Shows 🔧 icon
  - Displays tool name and arguments
  
- **Tool Result Message** (MessageType.ToolResult)
  - Shows 📋 icon
  - Displays execution result or error

### Request Configuration

```csharp
var request = new CodeGenerationRequest
{
    Description = userPrompt,
    Language = "Python",
    Provider = AiProvider.OpenAI,
    EnableTools = true,  // Enable MCP tools
    CurrentStepId = stepId  // For context-aware tool use
    // ... other parameters
};
```

## Adding New Tools

To add a new MCP tool:

### 1. Create Tool Class

```csharp
public class MyNewTool : IMcpTool
{
    public string Name => "my_new_tool";
    
    public string Description => "Description of what the tool does";
    
    public JsonDocument ParametersSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "param1": {
                "type": "string",
                "description": "Parameter description"
            }
        },
        "required": ["param1"]
    }
    """);
    
    public async Task<string> ExecuteAsync(
        string parameters, 
        Guid userId, 
        CancellationToken cancellationToken = default)
    {
        // Parse parameters
        var paramsDoc = JsonDocument.Parse(parameters);
        var param1 = paramsDoc.RootElement.GetProperty("param1").GetString();
        
        // Execute tool logic
        var result = new
        {
            success = true,
            data = "result data"
        };
        
        return JsonSerializer.Serialize(result);
    }
}
```

### 2. Register Tool

In `Program.cs`, add:

```csharp
mcpService.RegisterTool<MyNewTool>();
```

### 3. Add Dependencies

If your tool needs dependencies, inject them in the constructor:

```csharp
public MyNewTool(
    SomeService someService,
    ILogger<MyNewTool> logger)
{
    this.someService = someService;
    this.logger = logger;
}
```

## Security Considerations

1. **Authorization**: All tool executions receive the `userId` parameter for authorization checks
2. **Input Validation**: Tools validate all parameters before execution
3. **Error Handling**: Tools catch and return errors gracefully
4. **Scope Limitation**: Tools only access data the user has permission to see

## Testing

### Unit Tests

Located in `UnitTests/Automaton.Studio.Server.Tests/McpToolsTests.cs`

To add tests for new tools:

```csharp
[Test]
public void MyNewTool_ShouldExecuteSuccessfully()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddScoped<MyNewTool>();
    // Add required dependencies
    
    var serviceProvider = services.BuildServiceProvider();
    var tool = serviceProvider.GetRequiredService<MyNewTool>();
    
    // Act
    var parameters = JsonSerializer.Serialize(new { param1 = "test" });
    var result = await tool.ExecuteAsync(parameters, userId);
    
    // Assert
    Assert.That(result, Is.Not.Null);
}
```

### Manual Testing

1. Open Automaton Studio
2. Navigate to Custom Steps Designer
3. Open the AI Chat panel
4. Select OpenAI provider (tools only work with OpenAI currently)
5. Ask AI to read or test your code
6. Observe tool calls in the chat

Example prompts:
- "Can you read my current code and suggest improvements?"
- "Please test the code and tell me if it works"
- "Debug the issue - read the code, test it, and fix any problems"

## Extending to Other AI Providers

To add tool support for other providers:

1. Update the provider class to accept tools parameter
2. Implement provider-specific tool calling format
3. Handle tool calls in provider response parsing
4. Update `AiStepGenerationService` to support the provider

Example for Anthropic Claude:
```csharp
// Anthropic uses a different format for tools
// Refer to Anthropic's API documentation for specifics
```

## Future Enhancements

Potential additions to the MCP system:

1. **Flow Tools**
   - `get_flow_definition`: Read flow structure
   - `execute_flow`: Run a flow
   - `get_flow_logs`: Retrieve flow execution logs

2. **Data Tools**
   - `query_database`: Query application data
   - `get_statistics`: Retrieve dashboard statistics

3. **System Tools**
   - `list_runners`: Get available runners
   - `check_dependencies`: Verify step dependencies

4. **Advanced Features**
   - Async tool execution for long-running operations
   - Tool result caching
   - Tool usage analytics
   - Rate limiting per user/tool

## Troubleshooting

### Tools not appearing in AI responses

1. Verify tools are registered in `Program.cs`
2. Check that `EnableTools` is true in request
3. Ensure OpenAI provider is selected
4. Verify API key has access to models with function calling

### Tool execution failures

1. Check server logs for detailed error messages
2. Verify all required dependencies are registered in DI
3. Ensure parameters match the tool's schema
4. Confirm user has permission to access the resource

### Tool results not displayed in chat

1. Verify `ToolCall` and `ToolResult` message types are handled in UI
2. Check browser console for JavaScript errors
3. Ensure tool results are properly serialized

## Performance Considerations

- Tool execution is synchronous to maintain conversation flow
- Tools should complete quickly (< 5 seconds recommended)
- For long operations, consider returning status and polling separately
- Tool calls add latency to AI responses

## Related Documentation

- OpenAI Function Calling: https://platform.openai.com/docs/guides/function-calling
- Model Context Protocol: https://modelcontextprotocol.io/
- Automaton Custom Steps: See `docs/CustomSteps.md`
