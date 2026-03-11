using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Services;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Shared SSE streaming, session management, and history persistence logic
/// for all AI conversation controllers.
/// </summary>
[Authorize]
public abstract class AiConversationControllerBase : BaseController
{
    protected readonly IAutomatonMcpClient _mcpClient;
    protected readonly AiApiKeyService _aiApiKeyService;
    protected readonly IAiChatHistoryService _chatHistoryService;
    protected readonly AiProvidersConfig _aiProvidersConfig;
    protected readonly ISystemAiRateLimitService _rateLimitService;
    protected readonly AiProviderMetadataService _providerMetadataService;
    protected readonly ITokenBudgetService _tokenBudgetService;
    protected readonly ILogger _logger;

    protected AiConversationControllerBase(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        ISystemAiRateLimitService rateLimitService,
        AiProviderMetadataService providerMetadataService,
        ITokenBudgetService tokenBudgetService,
        ILogger logger)
    {
        _mcpClient = mcpClient;
        _aiApiKeyService = aiApiKeyService;
        _chatHistoryService = chatHistoryService;
        _aiProvidersConfig = aiProvidersConfig.Value;
        _rateLimitService = rateLimitService;
        _providerMetadataService = providerMetadataService;
        _tokenBudgetService = tokenBudgetService;
        _logger = logger;
    }

    // ── Subclass hooks ────────────────────────────────────────────────────

    /// <summary>Build the system prompt for the given request and session context.</summary>
    protected abstract Task<string> BuildSystemPromptAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        List<ConversationMessage>? history);

    /// <summary>
    /// Returns the effective mode to use for system-prompt suffix selection.
    /// Subclasses may override to downgrade the mode (e.g. Simple/Medium tiers always use "agent").
    /// </summary>
    protected virtual string GetEffectiveMode(BaseStreamConversationRequest request) => request.Mode;

    /// <summary>Return the tool provider to use for this resource type.</summary>
    protected abstract IMcpToolProvider? GetToolProvider();

    /// <summary>The ChatResourceType for session creation.</summary>
    protected abstract ChatResourceType ResourceType { get; }

    // ── Core streaming pipeline (shared) ─────────────────────────────────

    protected async Task ExecuteStreamAsync(
        BaseStreamConversationRequest request,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            var userId = GetUserId();

            // 1. Resolve provider/model/key
            var provider = request.Provider;
            var model = request.Model;

            if (string.IsNullOrWhiteSpace(model))
                model = await _providerMetadataService.GetPreferredModelAsync(provider);

            if (string.IsNullOrWhiteSpace(model))
            {
                await WriteErrorEvent($"No active model configured for provider '{provider}'.");
                return;
            }

            // In plan mode, prefer the designated planning model if one is configured
            if (string.Equals(request.Mode, "plan", StringComparison.OrdinalIgnoreCase))
            {
                var planningModel = await _providerMetadataService.GetPlanningModelAsync(provider);
                if (!string.IsNullOrWhiteSpace(planningModel))
                {
                    _logger.LogInformation("Plan mode: overriding model {Model} with planning model {PlanningModel}", model, planningModel);
                    model = planningModel;
                }
            }

            // Downgrade model if user has exceeded their monthly token allowance
            model = await _tokenBudgetService.GetEffectiveModelAsync(userId, provider, model);

            var apiKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(userId, provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                await WriteErrorEvent($"No API key configured for provider '{provider}'.");
                return;
            }

            if (!_rateLimitService.CheckAndRecordRequest(userId.ToString()))
            {
                await WriteErrorEvent("Rate limit exceeded. Please try again later.");
                return;
            }

            _logger.LogInformation(
                "Starting AI conversation for user {UserId}, provider {Provider}, model {Model}",
                userId, provider, model);

            // 2. Resolve session
            var sessionId = await ResolveSessionAsync(request, userId, provider, model);

            // 3. Load history
            var history = await LoadConversationHistoryAsync(sessionId, userId);

            // 4. Build system prompt (delegated to subclass) + append mode-specific instructions
            var systemPrompt = await BuildSystemPromptAsync(request, userId, sessionId, history);
            systemPrompt += ConversationModeFactory.From(GetEffectiveMode(request)).SystemPromptSuffix;

            // 5. Wire tool provider
            _mcpClient.SetToolProvider(GetToolProvider());

            // 6. Collect events for post-conversation persistence
            var pendingUpdates = new List<ConversationUpdatedEvent>();

            EventHandler<ConversationUpdatedEvent> eventHandler = (_, e) =>
            {
                _ = WriteConversationEvent(e);
                lock (pendingUpdates) { pendingUpdates.Add(e); }
            };
            _mcpClient.ConversationUpdated += eventHandler;

            // 7. Run conversation
            var conversationRequest = new ConversationRequest
            {
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                InitialMessage = request.Message,
                SystemPrompt = systemPrompt,
                Provider = provider,
                Model = model,
                ApiKey = apiKey,
                ConversationHistory = history,
                Context = BuildContextDictionary(request, userId),
                MaxIterations = _aiProvidersConfig.MaxIterations,
                MaxConsecutiveFailures = _aiProvidersConfig.MaxConsecutiveFailures,
                Mode = request.Mode
            };

            var result = await _mcpClient.StartConversationAsync(conversationRequest, cancellationToken);

            _mcpClient.ConversationUpdated -= eventHandler;

            // 8. Persist only on success — failed/cancelled attempts save nothing,
            //    so orphaned user messages and partial AI responses don't pollute history.
            if (result.IsSuccess)
            {
                // Save user message first (before AI responses to preserve chronological order)
                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = "user",
                    Content = request.Message,
                    MessageType = ChatMessageType.Text,
                    Metadata = "{}"
                }, userId);

                var savedToolCallIds = new HashSet<string>();
                foreach (var update in pendingUpdates)
                    await SaveConversationUpdateAsync(update, sessionId, userId, savedToolCallIds);

                await WriteEvent("done", new
                {
                    conversationId = result.ConversationId,
                    sessionId,
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

    // ── Session management ────────────────────────────────────────────────

    private async Task<Guid> ResolveSessionAsync(
        BaseStreamConversationRequest request, Guid userId, string provider, string model)
    {
        if (request.SessionId.HasValue)
            return request.SessionId.Value;

        if (request.ResourceId.HasValue)
        {
            var session = await _chatHistoryService.GetOrCreateActiveSessionAsync(
                ResourceType, request.ResourceId.Value, userId, provider, model);
            _logger.LogInformation("Auto-created session {SessionId} for resource {ResourceId}",
                session.Id, request.ResourceId);
            return session.Id;
        }

        var generalSession = await _chatHistoryService.CreateSessionAsync(new CreateChatSessionRequest
        {
            ResourceType = ResourceType,
            ResourceId = Guid.Empty,
            Title = "New Chat",
            Provider = provider,
            Model = model
        }, userId);
        return generalSession.Id;
    }

    // ── History loading ───────────────────────────────────────────────────

    protected async Task<List<ConversationMessage>?> LoadConversationHistoryAsync(Guid sessionId, Guid userId)
    {
        try
        {
            var messages = await _chatHistoryService.GetMessagesAsync(sessionId, userId);
            return messages
                .Where(m => m.MessageType == ChatMessageType.Text)
                .Select(m => new ConversationMessage(m.Role, m.Content))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load conversation history for session {SessionId}", sessionId);
            return null;
        }
    }

    // ── Context dictionary ────────────────────────────────────────────────

    protected virtual Dictionary<string, object> BuildContextDictionary(
        BaseStreamConversationRequest request, Guid userId)
    {
        var context = request.Context ?? new Dictionary<string, object>();
        if (request.ResourceId.HasValue)
            context["contextId"] = request.ResourceId.Value.ToString();
        if (request.ResourceId.HasValue)
            context["resourceId"] = request.ResourceId.Value;
        context["userId"] = userId;
        return context;
    }

    // ── Persistence ───────────────────────────────────────────────────────

    private async Task SaveConversationUpdateAsync(
        ConversationUpdatedEvent update,
        Guid sessionId,
        Guid userId,
        HashSet<string> savedToolCallIds)
    {
        try
        {
            foreach (var message in update.Messages)
            {
                if (message.IsStreaming) continue;

                // Token-only update (pure tool-call turn, no text) — accumulate tokens without saving a blank message.
                if (string.IsNullOrEmpty(message.Content))
                {
                    if (message.InputTokens.HasValue || message.OutputTokens.HasValue)
                        await _chatHistoryService.AccumulateTokensAsync(sessionId, userId, message.InputTokens, message.OutputTokens);
                    continue;
                }

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = message.Role,
                    Content = message.Content,
                    MessageType = ChatMessageType.Text,
                    InputTokens = message.InputTokens,
                    OutputTokens = message.OutputTokens,
                    Model = message.Model,
                    Metadata = "{}"
                }, userId);
            }

            foreach (var toolCall in update.ToolCalls)
            {
                if (!savedToolCallIds.Add(toolCall.Id)) continue;

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = "assistant",
                    Content = $"Executing tool: {toolCall.Name}",
                    MessageType = ChatMessageType.ToolExecution,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        toolCallId = toolCall.Id,
                        toolName = toolCall.Name,
                        arguments = toolCall.Arguments
                    })
                }, userId);
            }

            foreach (var toolResult in update.ToolResults)
            {
                if (!savedToolCallIds.Add($"result:{toolResult.ToolCallId}")) continue;

                await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
                {
                    SessionId = sessionId,
                    Role = "tool",
                    Content = toolResult.Result ?? string.Empty,
                    MessageType = toolResult.IsSuccess ? ChatMessageType.OperationComplete : ChatMessageType.CodeError,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        toolCallId = toolResult.ToolCallId,
                        success = toolResult.IsSuccess,
                        error = toolResult.ErrorMessage
                    })
                }, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving conversation update for session {SessionId}", sessionId);
        }
    }

    // ── SSE writing ───────────────────────────────────────────────────────

    private async Task WriteConversationEvent(ConversationUpdatedEvent update)
    {
        try
        {
            await WriteEvent("update", new
            {
                conversationId = update.ConversationId,
                messages = update.Messages,
                toolCalls = update.ToolCalls,
                toolResults = update.ToolResults,
                timestamp = update.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing conversation event");
        }
    }

    protected async Task WriteErrorEvent(string message) =>
        await WriteEvent("error", new { error = message });

    protected async Task WriteEvent(string eventType, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {json}\n\n");
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing SSE event");
        }
    }
}

// ── Shared base request ───────────────────────────────────────────────────

/// <summary>
/// Fields common to all AI conversation stream requests.
/// </summary>
public abstract class BaseStreamConversationRequest
{
    public string? ConversationId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ConversationMessage>? History { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ResourceId { get; set; }
    /// <summary>"plan" or "agent" (default)</summary>
    public string Mode { get; set; } = "agent";
}
