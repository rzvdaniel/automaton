using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;
using Automaton.Mcp.SystemPrompts;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Automaton.Studio.Server.Services.Mcp;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for AI conversation streaming via Server-Sent Events (SSE)
/// </summary>
[ApiController]
[Route("api/ai/conversation")]
[Authorize]
public class AiConversationController : BaseController
{
    private readonly IAutomatonMcpClient _mcpClient;
    private readonly AiApiKeyService _aiApiKeyService;
    private readonly IAiChatHistoryService _chatHistoryService;
    private readonly AiProvidersConfig _aiProvidersConfig;
    private readonly StepMcpToolProvider _stepToolProvider;
    private readonly AppMcpToolProvider _appToolProvider;
    private readonly IStepSystemPromptBuilder _stepPromptBuilder;
    private readonly IAppSystemPromptBuilder _appPromptBuilder;
    private readonly ILogger<AiConversationController> _logger;

    public AiConversationController(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        StepMcpToolProvider stepToolProvider,
        AppMcpToolProvider appToolProvider,
        IStepSystemPromptBuilder stepPromptBuilder,
        IAppSystemPromptBuilder appPromptBuilder,
        ILogger<AiConversationController> logger)
    {
        _mcpClient = mcpClient;
        _aiApiKeyService = aiApiKeyService;
        _chatHistoryService = chatHistoryService;
        _aiProvidersConfig = aiProvidersConfig.Value;
        _stepToolProvider = stepToolProvider;
        _appToolProvider = appToolProvider;
        _stepPromptBuilder = stepPromptBuilder;
        _appPromptBuilder = appPromptBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Stream AI conversation using Server-Sent Events (SSE)
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamConversation(
        [FromBody] StreamConversationRequest request,
        CancellationToken cancellationToken)
    {
        // Set SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering

        try
        {
            var userId = GetUserId();

            // 1. Get API key (secure - server-only)
            var apiKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(
                userId,
                request.Provider
            );

            if (string.IsNullOrEmpty(apiKey))
            {
                await WriteErrorEvent("No API key configured for this provider");
                return;
            }

            _logger.LogInformation(
                "Starting AI conversation for user {UserId}, provider {Provider}, model {Model}, resourceType {ResourceType}",
                userId, request.Provider, request.Model, request.ResourceType);

            // 2. Get or create session for chat history
            Guid sessionId;
            if (request.SessionId.HasValue)
            {
                sessionId = request.SessionId.Value;
            }
            else if (!string.IsNullOrEmpty(request.ResourceType) && request.ResourceId.HasValue)
            {
                // Parse ResourceType string to enum
                if (Enum.TryParse<ChatResourceType>(request.ResourceType, ignoreCase: true, out var resourceTypeEnum))
                {
                    // Auto-create session if resource context is provided
                    var session = await _chatHistoryService.GetOrCreateActiveSessionAsync(
                        resourceTypeEnum,
                        request.ResourceId.Value,
                        userId,
                        request.Provider,
                        request.Model);
                    sessionId = session.Id;
                    _logger.LogInformation("Auto-created session {SessionId} for resource {ResourceType}/{ResourceId}",
                        sessionId, request.ResourceType, request.ResourceId);
                }
                else
                {
                    // Invalid ResourceType, create general session
                    var session = await _chatHistoryService.CreateSessionAsync(new CreateChatSessionRequest
                    {
                        ResourceType = ChatResourceType.General,
                        ResourceId = Guid.Empty,
                        Title = "New Chat",
                        Provider = request.Provider,
                        Model = request.Model
                    }, userId);
                    sessionId = session.Id;
                    _logger.LogWarning("Invalid ResourceType '{ResourceType}', created general session {SessionId}",
                        request.ResourceType, sessionId);
                }
            }
            else
            {
                // No session and no resource context - create anonymous session
                var session = await _chatHistoryService.CreateSessionAsync(new CreateChatSessionRequest
                {
                    ResourceType = ChatResourceType.General,
                    ResourceId = Guid.Empty,
                    Title = "New Chat",
                    Provider = request.Provider,
                    Model = request.Model
                }, userId);
                sessionId = session.Id;
                _logger.LogInformation("Created general session {SessionId}", sessionId);
            }

            // 3. Load conversation history from database
            var history = await LoadConversationHistoryAsync(sessionId, userId);

            // 4. Build context-aware system prompt (with continuation context if needed)
            var systemPrompt = await BuildSystemPromptAsync(request, userId, history);

            // 5. Get tool provider based on resource type
            var toolProvider = GetToolProvider(request.ResourceType);

            // 6. Setup AutomatonMcpClient with tool provider
            _mcpClient.SetToolProvider(toolProvider);

            // 7. Save the user's message BEFORE starting the conversation
            await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = sessionId,
                Role = "user",
                Content = request.Message,
                MessageType = ChatMessageType.Text,
                Metadata = "{}"
            }, userId);
            _logger.LogInformation("Saved user message to session {SessionId}", sessionId);

            // 8. Collect updates for saving after conversation completes
            // We stream to client immediately, but only save to database AFTER conversation ends
            // This avoids DbContext threading issues (tool execution and chat history both use DbContext)
            var pendingUpdates = new List<ConversationUpdatedEvent>();

            // 9. Subscribe to conversation updates - stream to client in real-time
            EventHandler<ConversationUpdatedEvent> eventHandler = (sender, e) =>
            {
                // Stream to client immediately (SSE write doesn't use DbContext)
                // Fire-and-forget is OK here since Response.Body.WriteAsync is thread-safe
                _ = WriteConversationEvent(e);

                // Queue for database persistence after conversation ends
                lock (pendingUpdates)
                {
                    pendingUpdates.Add(e);
                }
            };
            _mcpClient.ConversationUpdated += eventHandler;

            // 10. Build conversation request with enhanced context
            var conversationRequest = new ConversationRequest
            {
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                InitialMessage = request.Message,
                SystemPrompt = systemPrompt,
                Provider = request.Provider,
                Model = request.Model,
                ApiKey = apiKey,
                ConversationHistory = history,
                Context = BuildContextDictionary(request, userId),
                MaxIterations = _aiProvidersConfig.MaxIterations,
                MaxConsecutiveFailures = _aiProvidersConfig.MaxConsecutiveFailures
            };

            // 11. Start conversation - this runs synchronously within the provider's loop
            var result = await _mcpClient.StartConversationAsync(
                conversationRequest,
                cancellationToken
            );

            // 12. Unsubscribe from events
            _mcpClient.ConversationUpdated -= eventHandler;

            // 13. Now that conversation is complete, save all updates to database
            // This happens sequentially - no concurrency issues with DbContext
            foreach (var update in pendingUpdates)
            {
                await SaveConversationUpdateAsync(update, sessionId, userId);
            }

            // 14. Write completion event
            if (result.IsSuccess)
            {
                await WriteEvent("done", new
                {
                    conversationId = result.ConversationId,
                    sessionId = sessionId,
                    success = true
                });
            }
            else
            {
                await WriteErrorEvent(result.ErrorMessage ?? "Conversation failed");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AI conversation stream cancelled");
            await WriteEvent("cancelled", new { message = "Stream cancelled by client" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI conversation stream");
            await WriteErrorEvent($"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// Build context-aware system prompt based on resource type
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(StreamConversationRequest request, Guid userId, List<ConversationMessage>? conversationHistory)
    {
        // Build base system prompt based on resource type
        var baseSystemPrompt = request.ResourceType?.ToLowerInvariant() switch
        {
            "customstep" => _stepPromptBuilder.BuildSystemPrompt(request.Language ?? "Python"),
            "webapp" => _appPromptBuilder.BuildSystemPrompt(),
            _ => "You are a helpful AI assistant for the Automaton platform."
        };

        // Add continuation context if this is a restored session with history
        // This prevents the AI from being overly cautious in ongoing conversations
        var isRestoredSession = conversationHistory != null && conversationHistory.Count > 0;
        
        if (isRestoredSession)
        {
            _logger.LogInformation(
                "Adding continuation prompt for session {SessionId} with {MessageCount} historical messages",
                request.SessionId, conversationHistory!.Count);
            
            return $"{baseSystemPrompt}\n\nIMPORTANT: This is a continuation of a previous conversation. Review the conversation history provided above and continue naturally from where we left off. Do not restart, regenerate, or repeat work that has already been completed unless explicitly asked to do so.";
        }

        return baseSystemPrompt;
    }

    /// <summary>
    /// Get the appropriate tool provider based on resource type
    /// </summary>
    private IMcpToolProvider? GetToolProvider(string? resourceType)
    {
        return resourceType?.ToLowerInvariant() switch
        {
            "customstep" => _stepToolProvider,
            "webapp" => _appToolProvider,
            _ => null
        };
    }

    /// <summary>
    /// Load conversation history from database if session exists
    /// </summary>
    private async Task<List<ConversationMessage>?> LoadConversationHistoryAsync(Guid? sessionId, Guid userId)
    {
        if (!sessionId.HasValue)
            return null;

        try
        {
            var messages = await _chatHistoryService.GetMessagesAsync(sessionId.Value, userId);
            
            // Convert ChatMessageDto to ConversationMessage
            return messages
                .Where(m => m.MessageType == ChatMessageType.Text) // Only include regular text messages in history
                .Select(m => new ConversationMessage(m.Role, m.Content))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load conversation history for session {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Build enhanced context dictionary with resource information
    /// </summary>
    private Dictionary<string, object> BuildContextDictionary(StreamConversationRequest request, Guid userId)
    {
        var context = request.Context ?? new Dictionary<string, object>();

        // Add contextId (resource ID formatted as string)
        if (request.ResourceId.HasValue)
            context["contextId"] = request.ResourceId.Value.ToString();

        // Add essential context for tool execution
        if (request.ResourceType != null)
            context["resourceType"] = request.ResourceType;
        if (request.ResourceId.HasValue)
            context["resourceId"] = request.ResourceId.Value;
        if (request.Language != null)
            context["language"] = request.Language;

        context["userId"] = userId;

        return context;
    }

    /// <summary>
    /// Save conversation updates (messages, tool calls) to database during streaming
    /// </summary>
    private async Task SaveConversationUpdateAsync(
        ConversationUpdatedEvent update,
        Guid sessionId,
        Guid userId)
    {
        try
        {
            // Save regular messages - only save complete messages (not streaming partial content)
            foreach (var message in update.Messages)
            {
                _logger.LogInformation(
                    "Processing message: Role={Role}, IsStreaming={IsStreaming}, ContentLength={ContentLength}, ContentPreview={Preview}",
                    message.Role,
                    message.IsStreaming,
                    message.Content?.Length ?? 0,
                    message.Content?.Length > 50 ? message.Content.Substring(0, 50) + "..." : message.Content);

                // Skip streaming (partial) messages - only persist complete messages
                if (message.IsStreaming)
                {
                    _logger.LogDebug("Skipping streaming message (partial content), will save when complete");
                    continue;
                }

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = message.Role,
                    Content = message.Content,
                    MessageType = ChatMessageType.Text,
                    Metadata = "{}"
                }, userId);

                _logger.LogInformation("Saved complete {Role} message to session {SessionId}",
                    message.Role, sessionId);
            }

            // Save tool calls as special messages
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
                    Role = "assistant",
                    Content = $"Executing tool: {toolCall.Name}",
                    MessageType = ChatMessageType.ToolExecution,
                    Metadata = metadata
                }, userId);
            }

            // Save tool results
            foreach (var toolResult in update.ToolResults)
            {
                var metadata = JsonSerializer.Serialize(new
                {
                    toolCallId = toolResult.ToolCallId,
                    success = toolResult.IsSuccess,
                    error = toolResult.ErrorMessage
                });

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = "tool",
                    Content = toolResult.Result ?? string.Empty,
                    MessageType = toolResult.IsSuccess ? ChatMessageType.OperationComplete : ChatMessageType.CodeError,
                    Metadata = metadata
                }, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving conversation update for session {SessionId}", sessionId);
            // Don't throw - history saving shouldn't break streaming
        }
    }

    /// <summary>
    /// Save only tool calls and results (can be saved during streaming)
    /// </summary>
    private async Task SaveToolUpdatesAsync(
        ConversationUpdatedEvent update,
        Guid sessionId,
        Guid userId)
    {
        try
        {
            // Save tool calls as special messages
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
                    Role = "assistant",
                    Content = $"Executing tool: {toolCall.Name}",
                    MessageType = ChatMessageType.ToolExecution,
                    Metadata = metadata
                }, userId);

                _logger.LogInformation("Saved tool call {ToolName} to session {SessionId}", toolCall.Name, sessionId);
            }

            // Save tool results
            foreach (var toolResult in update.ToolResults)
            {
                var metadata = JsonSerializer.Serialize(new
                {
                    toolCallId = toolResult.ToolCallId,
                    success = toolResult.IsSuccess,
                    error = toolResult.ErrorMessage
                });

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = "tool",
                    Content = toolResult.Result ?? string.Empty,
                    MessageType = toolResult.IsSuccess ? ChatMessageType.OperationComplete : ChatMessageType.CodeError,
                    Metadata = metadata
                }, userId);

                _logger.LogInformation("Saved tool result for {ToolCallId} to session {SessionId}", toolResult.ToolCallId, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving tool updates for session {SessionId}", sessionId);
            // Don't throw - history saving shouldn't break streaming
        }
    }

    /// <summary>
    /// Save complete text messages (called after conversation ends while DbContext is still valid)
    /// </summary>
    private async Task SaveTextMessagesAsync(
        ConversationUpdatedEvent update,
        Guid sessionId,
        Guid userId)
    {
        try
        {
            foreach (var message in update.Messages)
            {
                _logger.LogInformation(
                    "Saving complete message: Role={Role}, ContentLength={ContentLength}, ContentPreview={Preview}",
                    message.Role,
                    message.Content?.Length ?? 0,
                    message.Content?.Length > 50 ? message.Content.Substring(0, 50) + "..." : message.Content);

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = message.Role,
                    Content = message.Content,
                    MessageType = ChatMessageType.Text,
                    Metadata = "{}"
                }, userId);

                _logger.LogInformation("Saved complete {Role} message to session {SessionId}", message.Role, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving text messages for session {SessionId}", sessionId);
            // Don't throw - history saving shouldn't break streaming
        }
    }

    /// <summary>
    /// Write a conversation update event
    /// </summary>
    private async Task WriteConversationEvent(ConversationUpdatedEvent update)
    {
        try
        {
            var eventData = new
            {
                conversationId = update.ConversationId,
                messages = update.Messages,
                toolCalls = update.ToolCalls,
                toolResults = update.ToolResults,
                timestamp = update.Timestamp
            };

            await WriteEvent("update", eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing conversation event");
        }
    }

    /// <summary>
    /// Write an error event
    /// </summary>
    private async Task WriteErrorEvent(string message)
    {
        await WriteEvent("error", new { error = message });
    }

    /// <summary>
    /// Write an SSE event
    /// </summary>
    private async Task WriteEvent(string eventType, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var message = $"event: {eventType}\ndata: {json}\n\n";
            var bytes = Encoding.UTF8.GetBytes(message);

            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing SSE event");
        }
    }
}

/// <summary>
/// Request to stream an AI conversation
/// </summary>
public class StreamConversationRequest
{
    /// <summary>
    /// Conversation ID (optional, will be generated if not provided)
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// AI provider name (e.g., "OpenAI", "Anthropic")
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// AI model name (e.g., "gpt-4", "claude-3-opus")
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// User's message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Conversation history (for continuing conversations)
    /// </summary>
    public List<ConversationMessage>? History { get; set; }

    /// <summary>
    /// Additional context (e.g., resourceType, resourceId, runnerId)
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>
    /// Chat session ID for persistence (optional)
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Resource type for context-aware prompts and tools ("CustomStep", "WebApp")
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource ID for tool execution context
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>
    /// Programming language for Step context (e.g., "Python", "PowerShell")
    /// </summary>
    public string? Language { get; set; }
}
