using System.Text.Json;
using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for streaming AI conversations with real-time updates via SSE
/// </summary>
public class AiStreamingService
{
    private readonly ILogger<AiStreamingService> _logger;
    private readonly AiApiKeyService _apiKeyService;
    private readonly IServiceProvider _serviceProvider;

    public AiStreamingService(
        ILogger<AiStreamingService> logger,
        AiApiKeyService apiKeyService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _apiKeyService = apiKeyService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Start a streaming AI conversation
    /// </summary>
    public async Task StreamConversationAsync(
        StreamConversationRequest request,
        Guid userId,
        Func<StreamChunk, Task> onChunk,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Starting streaming conversation {ConversationId} for resource {ResourceType}/{ResourceId}",
                request.ConversationId, request.ResourceType, request.ResourceId);

            // Get the appropriate API key
            var apiKey = await GetApiKeyAsync(request.ApiKeyId, request.ProviderName, userId);
            if (string.IsNullOrEmpty(apiKey))
            {
                await onChunk(new StreamChunk
                {
                    Type = StreamChunkType.Error,
                    Content = "No API key configured for this provider. Please add an API key in Settings."
                });
                return;
            }

            // Create tool provider based on resource type
            var toolProvider = CreateToolProvider(request.ResourceType, request.ResourceId, request.Context);

            // Create MCP client
            using var mcpClient = new AutomatonMcpClient(
                _serviceProvider.GetRequiredService<ILogger<AutomatonMcpClient>>(),
                toolProvider);

            // Subscribe to conversation updates
            mcpClient.ConversationUpdated += async (sender, e) =>
            {
                await HandleConversationUpdate(e, onChunk);
            };

            // Build system prompt based on resource type
            var systemPrompt = BuildSystemPrompt(request.ResourceType, request.Language, request.Context);

            // Create conversation request
            var conversationRequest = new ConversationRequest
            {
                ConversationId = request.ConversationId,
                InitialMessage = request.Message,
                SystemPrompt = systemPrompt,
                Provider = request.ProviderName,
                Model = request.Model ?? GetDefaultModel(request.ProviderName),
                ApiKey = apiKey,
                ConversationHistory = request.History?.Select(h => new ConversationMessage(h.Role, h.Content)).ToList(),
                Context = request.Context ?? new Dictionary<string, object>()
            };

            // Add resource context
            if (!string.IsNullOrEmpty(request.ResourceId))
            {
                conversationRequest.Context["contextId"] = request.ResourceId;
                conversationRequest.Context["resourceType"] = request.ResourceType;
            }
            if (!string.IsNullOrEmpty(request.Language))
            {
                conversationRequest.Context["language"] = request.Language;
            }

            // Start the conversation
            var result = await mcpClient.StartConversationAsync(conversationRequest, cancellationToken);

            if (!result.IsSuccess)
            {
                await onChunk(new StreamChunk
                {
                    Type = StreamChunkType.Error,
                    Content = result.ErrorMessage ?? "Unknown error occurred"
                });
            }

            // Send completion
            await onChunk(new StreamChunk
            {
                Type = StreamChunkType.Complete
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming conversation {ConversationId} was cancelled", request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming conversation {ConversationId}", request.ConversationId);
            await onChunk(new StreamChunk
            {
                Type = StreamChunkType.Error,
                Content = ex.Message
            });
        }
    }

    private async Task HandleConversationUpdate(ConversationUpdatedEvent e, Func<StreamChunk, Task> onChunk)
    {
        // Handle text messages
        foreach (var message in e.Messages)
        {
            if (message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                // Check if this looks like code
                if (ContainsCodeBlock(message.Content))
                {
                    await onChunk(new StreamChunk
                    {
                        Type = StreamChunkType.CodeGenerated,
                        Content = message.Content,
                        Code = ExtractCode(message.Content),
                        Language = DetectLanguage(message.Content)
                    });
                }
                else
                {
                    await onChunk(new StreamChunk
                    {
                        Type = StreamChunkType.Text,
                        Content = message.Content
                    });
                }
            }
        }

        // Handle tool calls
        foreach (var toolCall in e.ToolCalls)
        {
            await onChunk(new StreamChunk
            {
                Type = StreamChunkType.ToolCall,
                ToolCall = new ToolCallInfo
                {
                    Id = toolCall.Id,
                    Name = toolCall.Name,
                    Description = GetToolDescription(toolCall.Name),
                    Arguments = toolCall.Arguments ?? new Dictionary<string, object?>()
                }
            });
        }

        // Handle tool results
        foreach (var toolResult in e.ToolResults)
        {
            await onChunk(new StreamChunk
            {
                Type = StreamChunkType.ToolResult,
                ToolResult = new ToolResultInfo
                {
                    ToolId = toolResult.ToolCallId,
                    ToolName = GetToolNameFromResult(toolResult),
                    IsSuccess = toolResult.IsSuccess,
                    Data = toolResult.Result,
                    Error = toolResult.ErrorMessage
                }
            });
        }
    }

    private async Task<string?> GetApiKeyAsync(Guid? apiKeyId, string providerName, Guid userId)
    {
        // Try to get API key from specified ID
        if (apiKeyId.HasValue)
        {
            return await _apiKeyService.GetDecryptedApiKeyAsync(apiKeyId.Value, userId);
        }

        // Try to get first available key for provider
        var keys = await _apiKeyService.GetAiApiKeysByProviderAsync(userId, providerName);
        var firstKey = keys.FirstOrDefault();
        if (firstKey != null)
        {
            return await _apiKeyService.GetDecryptedApiKeyAsync(firstKey.Id, userId);
        }

        // Try environment variables
        var envKeys = _apiKeyService.GetEnvironmentApiKeys();
        if (envKeys.TryGetValue(providerName, out var envKey) && !string.IsNullOrEmpty(envKey))
        {
            return envKey;
        }

        return null;
    }

    private IMcpToolProvider CreateToolProvider(string resourceType, string? resourceId, Dictionary<string, object>? context)
    {
        // Return a tool provider based on resource type
        return resourceType.ToLowerInvariant() switch
        {
            "app" => new AppMcpToolProvider(_serviceProvider, resourceId, context),
            "step" => new StepMcpToolProvider(_serviceProvider, resourceId, context),
            "flow" => new FlowMcpToolProvider(_serviceProvider, resourceId, context),
            _ => new DefaultMcpToolProvider()
        };
    }

    private string BuildSystemPrompt(string resourceType, string? language, Dictionary<string, object>? context)
    {
        return resourceType.ToLowerInvariant() switch
        {
            "app" => BuildAppSystemPrompt(context),
            "step" => BuildStepSystemPrompt(language, context),
            "flow" => BuildFlowSystemPrompt(context),
            _ => "You are an AI assistant helping with automation tasks."
        };
    }

    private string BuildAppSystemPrompt(Dictionary<string, object>? context)
    {
        return @"You are an expert full-stack web developer helping to build web applications in Automaton Studio.

## YOUR CAPABILITIES
- Generate HTML, CSS, and JavaScript code for web apps
- Create responsive, accessible UI components
- Implement interactive features and form handling
- Connect frontend to backend steps for data processing

## TOOLS AVAILABLE
- get_app_files: Read current app files (HTML, CSS, JS)
- save_app_file: Save/update app files
- list_app_files: Get file tree
- get_linked_steps: Get backend steps linked to this app
- create_backend_step: Create a new backend step for data processing
- execute_backend_step: Test a backend step

## WORKFLOW
1. When asked to modify, first use get_app_files to see current code
2. Generate clean, well-commented code
3. Use save_app_file to save changes
4. Test functionality and iterate

## CODE STYLE
- Use modern JavaScript (ES6+)
- Include comments explaining logic
- Follow accessibility best practices
- Use semantic HTML elements
- Keep styles organized and responsive

Generate code step by step, explaining your approach.";
    }

    private string BuildStepSystemPrompt(string? language, Dictionary<string, object>? context)
    {
        var lang = language ?? "Python";
        return $@"You are an expert automation engineer helping to create custom workflow steps in {lang}.

## YOUR CAPABILITIES
- Generate {lang} code for automation steps
- Define input and output variables
- Handle errors gracefully
- Integrate with external APIs and services

## TOOLS AVAILABLE
- get_current_code: Read the current step code
- get_step_variables: Get defined input/output variables
- suggest_variables: Suggest new input/output variables
- save_code: Save the step code
- execute_code: Test the step execution

## WORKFLOW
1. When modifying, first use get_current_code to see existing code
2. Check get_step_variables for defined variables
3. Generate clean, well-documented code
4. Use save_code to save changes
5. Test with execute_code

## CODE STYLE for {lang}
- Include error handling with try/except blocks
- Log important steps with print() statements
- Access input variables from the context
- Set output variables at the end
- Add comments explaining complex logic

Generate code step by step, testing as you go.";
    }

    private string BuildFlowSystemPrompt(Dictionary<string, object>? context)
    {
        return @"You are an expert automation architect helping to design and create workflows in Automaton Studio.

## YOUR CAPABILITIES
- Design multi-step automation workflows
- Select and configure appropriate steps
- Define variable mappings between steps
- Handle conditional logic and error paths

## TOOLS AVAILABLE
- search_steps: Find existing reusable steps
- create_step: Create a new custom step
- get_flow_definition: Read current flow structure
- update_flow: Modify flow structure
- test_flow: Dry-run the flow

## WORKFLOW
1. Understand the user's automation goal
2. Search for existing steps that match requirements
3. Create custom steps only when needed
4. Define proper variable mappings
5. Test the complete flow

Focus on creating efficient, maintainable workflows.";
    }

    private string GetDefaultModel(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "openai" => "gpt-4o",
            "anthropic" => "claude-3-5-sonnet-20241022",
            "google" => "gemini-1.5-pro",
            _ => "gpt-4o"
        };
    }

    private bool ContainsCodeBlock(string content)
    {
        return content.Contains("```") ||
               content.Contains("def ") ||
               content.Contains("function ") ||
               content.Contains("class ") ||
               content.Contains("<html") ||
               content.Contains("<!DOCTYPE");
    }

    private string ExtractCode(string content)
    {
        // Extract code from markdown code blocks
        var lines = content.Split('\n');
        var inCodeBlock = false;
        var codeLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }
            if (inCodeBlock)
            {
                codeLines.Add(line);
            }
        }

        return codeLines.Count > 0 ? string.Join('\n', codeLines) : content;
    }

    private string DetectLanguage(string content)
    {
        if (content.Contains("```python") || content.Contains("def ")) return "python";
        if (content.Contains("```javascript") || content.Contains("function ")) return "javascript";
        if (content.Contains("```typescript")) return "typescript";
        if (content.Contains("```html") || content.Contains("<html")) return "html";
        if (content.Contains("```css")) return "css";
        if (content.Contains("```csharp") || content.Contains("class ")) return "csharp";
        return "text";
    }

    private string GetToolDescription(string toolName)
    {
        return toolName switch
        {
            "get_app_files" => "Reading app files...",
            "save_app_file" => "Saving file...",
            "list_app_files" => "Listing files...",
            "get_current_code" => "Reading current code...",
            "save_code" => "Saving code...",
            "execute_code" => "Testing code...",
            "get_step_variables" => "Getting variables...",
            "suggest_variables" => "Suggesting variables...",
            "search_steps" => "Searching for steps...",
            "create_step" => "Creating step...",
            _ => $"Executing {toolName}..."
        };
    }

    private string GetToolNameFromResult(ToolResult result)
    {
        // In a real implementation, we'd track tool call IDs to names
        return "tool";
    }
}

/// <summary>
/// Request model for streaming conversation
/// </summary>
public class StreamConversationRequest
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string ResourceType { get; set; } = "app"; // app, step, flow
    public string? ResourceId { get; set; }
    public string? Language { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProviderName { get; set; } = "OpenAI";
    public string? Model { get; set; }
    public Guid? ApiKeyId { get; set; }
    public List<MessageHistoryItem>? History { get; set; }
    public Dictionary<string, object>? Context { get; set; }
}

public class MessageHistoryItem
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Stream chunk for SSE
/// </summary>
public class StreamChunk
{
    public StreamChunkType Type { get; set; }
    public string? Content { get; set; }
    public string? Code { get; set; }
    public string? Language { get; set; }
    public string? CodeId { get; set; }
    public string? FileName { get; set; }
    public ToolCallInfo? ToolCall { get; set; }
    public ToolResultInfo? ToolResult { get; set; }
}

public enum StreamChunkType
{
    Text,
    Reasoning,
    ToolCall,
    ToolResult,
    CodeGenerated,
    Complete,
    Error
}

public class ToolCallInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

public class ToolResultInfo
{
    public string ToolId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Data { get; set; }
    public string? Error { get; set; }
}
