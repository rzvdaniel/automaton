using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Services;
using Automaton.Mcp.SystemPrompts;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
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
    protected readonly IAiProvidersSettingsService _settingsService;
    protected readonly ISystemAiRateLimitService _rateLimitService;
    protected readonly AiProviderMetadataService _providerMetadataService;
    protected readonly ITokenBudgetService _tokenBudgetService;
    protected readonly IAppSystemPromptBuilder _appPromptBuilder;
    protected readonly ILogger _logger;

    /// <summary>
    /// Set by subclasses during <see cref="BuildSystemPromptAsync"/> to influence the
    /// iteration limit used for this conversation.
    /// </summary>
    protected ComplexityTier? _resolvedTier;
    protected string? _effectiveVerificationStrategy;
    protected string? _effectiveExecutionPolicy;

    protected AiConversationControllerBase(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IAiProvidersSettingsService settingsService,
        ISystemAiRateLimitService rateLimitService,
        AiProviderMetadataService providerMetadataService,
        ITokenBudgetService tokenBudgetService,
        IAppSystemPromptBuilder appPromptBuilder,
        ILogger logger)
    {
        _mcpClient = mcpClient;
        _aiApiKeyService = aiApiKeyService;
        _chatHistoryService = chatHistoryService;
        _settingsService = settingsService;
        _rateLimitService = rateLimitService;
        _providerMetadataService = providerMetadataService;
        _tokenBudgetService = tokenBudgetService;
        _appPromptBuilder = appPromptBuilder;
        _logger = logger;
    }

    // ── Subclass hooks ────────────────────────────────────────────────────

    /// <summary>Build the system prompt parts for the given request and session context.</summary>
    protected abstract Task<SystemPromptParts> BuildSystemPromptAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        List<ConversationMessage>? history);

    /// <summary>Return the tool provider to use for this resource type.</summary>
    protected abstract IMcpToolProvider? GetToolProvider();

    /// <summary>The ChatResourceType for session creation.</summary>
    protected abstract ChatResourceType ResourceType { get; }

    /// <summary>
    /// Called before the AI conversation starts. Subclasses may return an opaque token
    /// (e.g. a snapshot ID) that is passed to <see cref="OnAfterConversationAsync"/>.
    /// </summary>
    protected virtual Task<object?> OnBeforeConversationAsync(
        BaseStreamConversationRequest request, Guid userId, Guid sessionId) => Task.FromResult<object?>(null);

    /// <summary>
    /// Called after a successful AI conversation. <paramref name="token"/> is whatever
    /// <see cref="OnBeforeConversationAsync"/> returned.
    /// </summary>
    protected virtual Task OnAfterConversationAsync(
        BaseStreamConversationRequest request, Guid userId, ConversationResult result, object? token) => Task.CompletedTask;

    // ── Core streaming pipeline (shared) ─────────────────────────────────

    protected async Task ExecuteStreamAsync(
        BaseStreamConversationRequest request,
        CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no");

        Guid userId = Guid.Empty;
        Guid? sessionId = null;
        bool isSystemKey = false;
        _effectiveVerificationStrategy = null;
        _effectiveExecutionPolicy = null;
        var pendingUpdates = new ConcurrentQueue<ConversationUpdatedEvent>();
        EventHandler<ConversationUpdatedEvent>? eventHandler = null;

        try
        {
            userId = GetUserId();

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

            // Downgrade model if user has exceeded their monthly token allowance
            model = await _tokenBudgetService.GetEffectiveModelAsync(userId, provider, model);

            var aiConfig = await _settingsService.GetEffectiveConfigAsync();

            var modelMaxTokens = await _providerMetadataService.GetModelMaxTokensAsync(provider, model);
            var modelExtraBodyJson = await _providerMetadataService.GetModelExtraBodyJsonAsync(provider, model);

            (var apiKey, isSystemKey) = await _aiApiKeyService.GetDecryptedApiKeyWithSourceAsync(userId, provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                await WriteErrorEvent($"No API key configured for provider '{provider}'.");
                return;
            }

            _logger.LogInformation(
                "Starting AI conversation for user {UserId}, provider {Provider}, model {Model}, systemKey={IsSystemKey}",
                userId, provider, model, isSystemKey);

            // 2. Resolve session
            sessionId = await ResolveSessionAsync(request, userId, provider, model);
            var resolvedSessionId = sessionId.Value;

            // 3. Load history
            var history = await LoadConversationHistoryAsync(resolvedSessionId, userId);
            var compactedHistory = ConversationHistoryCompactor.Compact(
                history,
                aiConfig.MaxConversationHistoryMessages,
                aiConfig.HistoryCompaction);

            // 4. Build system prompt parts (delegated to subclass) + append mode-specific instructions
            var promptParts = await BuildSystemPromptAsync(request, userId, resolvedSessionId, compactedHistory);
            var mode = ConversationModeFactory.From(request.Mode);
            var modePromptSuffix = mode.SystemPromptSuffix;
            // Append mode suffix to the base prompt so it stays within the cached block
            var partsWithMode = promptParts with { BasePrompt = promptParts.BasePrompt + modePromptSuffix };

            // 5. Wire tool provider
            _mcpClient.SetToolProvider(GetToolProvider());

            // 6. Pre-conversation hook (e.g. create version snapshot)
            var conversationToken = await OnBeforeConversationAsync(request, userId, resolvedSessionId);

            // 6b. Persist user message immediately — before the AI responds.
            //     Industry standard: the user's message belongs to history the moment it is sent,
            //     regardless of whether the AI succeeds, fails, or the stream is cancelled.
            await EnsurePolicyMarkerMessagesAsync(resolvedSessionId, userId);
            await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
            {
                SessionId = resolvedSessionId,
                Role = "user",
                Content = request.Message,
                MessageType = ChatMessageType.Text,
                Metadata = "{}"
            }, userId);

            // 7. Subscribe to events and run conversation
            eventHandler = (_, e) =>
            {
                _ = WriteConversationEvent(e);
                pendingUpdates.Enqueue(e);
            };
            _mcpClient.ConversationUpdated += eventHandler;

            // 8. Run conversation
            var conversationRequest = new ConversationRequest
            {
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                InitialMessage = request.Message,
                SystemPrompt = partsWithMode.Full,
                SystemPromptParts = partsWithMode,
                Provider = provider,
                Model = model,
                ApiKey = apiKey,
                ConversationHistory = compactedHistory,
                Context = BuildContextDictionary(request, userId),
                MaxIterations = mode.Name == ConversationModes.Autonomous
                    ? IterationLimits.ResolveForAutonomous(
                        globalOverride:     aiConfig.MaxIterations,
                        autonomousOverride: aiConfig.IterationLimits.Autonomous)
                    : IterationLimits.Resolve(
                        _resolvedTier,
                        globalOverride: aiConfig.MaxIterations,
                        tierOverride:   aiConfig.IterationLimits.For(_resolvedTier)),
                MaxConsecutiveFailures = aiConfig.MaxConsecutiveFailures,
                MaxTokens = modelMaxTokens,
                MaxRateLimitRetries = aiConfig.RateLimitRetry.MaxRetries,
                RateLimitRetryInitialDelaySeconds = aiConfig.RateLimitRetry.InitialDelaySeconds,
                StreamingTimeoutMinutes = aiConfig.Streaming.TimeoutMinutes,
                Mode = request.Mode,
                ExtraBodyJson = modelExtraBodyJson
            };

            var result = await _mcpClient.StartConversationAsync(conversationRequest, cancellationToken);

            var updatesSnapshot = new List<ConversationUpdatedEvent>(pendingUpdates.ToArray());

            // 9. Persist AI responses and usage on success.
            if (result.IsSuccess)
            {
                var savedToolCallIds = new HashSet<string>();
                foreach (var update in updatesSnapshot)
                    await SaveConversationUpdateAsync(update, resolvedSessionId, userId, savedToolCallIds);

                if (result.ApiCallCount > 0)
                    await _chatHistoryService.IncrementApiCallsAsync(resolvedSessionId, userId, result.ApiCallCount);

                // 8b. Post-conversation hook (e.g. update version summary)
                await OnAfterConversationAsync(request, userId, result, conversationToken);

                await WriteEvent("done", new
                {
                    conversationId = result.ConversationId,
                    sessionId,
                    model,
                    effectiveVerificationStrategy = _effectiveVerificationStrategy,
                    effectiveExecutionPolicy = _effectiveExecutionPolicy,
                    success = true
                });
            }
            else
            {
                // Persist usage metrics even when generation fails so spent tokens are not lost.
                await FlushPendingTokenUsageAsync(updatesSnapshot, sessionId.Value, userId);

                var errorMsg = result.ErrorMessage ?? "Conversation failed";
                if (IsRateLimitError(errorMsg))
                    await WriteErrorEvent(RateLimitMessage(isSystemKey), "RATE_LIMIT");
                else
                    await WriteErrorEvent(errorMsg);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("AI conversation stream cancelled");

            // Client cancelled/closed stream — still persist consumed token usage.
            await FlushPendingTokenUsageIfPossibleAsync(sessionId, userId, pendingUpdates);

            await WriteEvent("cancelled", new { message = "Stream cancelled by client" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI conversation stream");

            // Persist token usage for unexpected failures as best-effort accounting.
            await FlushPendingTokenUsageIfPossibleAsync(sessionId, userId, pendingUpdates);

            if (IsRateLimitError(ex.Message))
                await WriteErrorEvent(RateLimitMessage(isSystemKey), "RATE_LIMIT");
            else
                await WriteErrorEvent(ex.Message);
        }
        finally
        {
            if (eventHandler != null)
                _mcpClient.ConversationUpdated -= eventHandler;
        }
    }

    private static bool IsRateLimitError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("rate_limit",        StringComparison.OrdinalIgnoreCase) ||
               message.Contains("rate limited",      StringComparison.OrdinalIgnoreCase) ||
               message.Contains("429",               StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Rate limit",        StringComparison.OrdinalIgnoreCase);
    }

    private static string RateLimitMessage(bool isSystemKey) =>
        isSystemKey
            ? "The shared demo key has hit the provider's rate limit. Add your own API key to continue without restrictions."
            : "Your API key has hit the provider's rate limit. Please wait a moment and try again.";

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
                    if (message.InputTokens.HasValue || message.OutputTokens.HasValue || message.CacheReadTokens.HasValue || message.CacheCreationTokens.HasValue)
                        await _chatHistoryService.AccumulateTokensAsync(sessionId, userId, message.InputTokens, message.OutputTokens, message.CacheReadTokens, message.CacheCreationTokens);
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
                    CacheReadTokens = message.CacheReadTokens,
                    CacheCreationTokens = message.CacheCreationTokens,
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

    private async Task FlushPendingTokenUsageAsync(
        IReadOnlyList<ConversationUpdatedEvent> updates,
        Guid sessionId,
        Guid userId)
    {
        if (updates.Count == 0)
            return;

        var inputTokens = 0;
        var outputTokens = 0;
        var cacheReadTokens = 0;
        var cacheCreationTokens = 0;
        var hasAny = false;

        foreach (var update in updates)
        {
            foreach (var message in update.Messages)
            {
                if (message.InputTokens.HasValue)
                {
                    inputTokens += message.InputTokens.Value;
                    hasAny = true;
                }

                if (message.OutputTokens.HasValue)
                {
                    outputTokens += message.OutputTokens.Value;
                    hasAny = true;
                }

                if (message.CacheReadTokens.HasValue)
                {
                    cacheReadTokens += message.CacheReadTokens.Value;
                    hasAny = true;
                }

                if (message.CacheCreationTokens.HasValue)
                {
                    cacheCreationTokens += message.CacheCreationTokens.Value;
                    hasAny = true;
                }
            }
        }

        if (!hasAny)
            return;

        await _chatHistoryService.AccumulateTokensAsync(
            sessionId,
            userId,
            inputTokens > 0 ? inputTokens : null,
            outputTokens > 0 ? outputTokens : null,
            cacheReadTokens > 0 ? cacheReadTokens : null,
            cacheCreationTokens > 0 ? cacheCreationTokens : null);
    }

    private async Task FlushPendingTokenUsageIfPossibleAsync(
        Guid? sessionId,
        Guid userId,
        ConcurrentQueue<ConversationUpdatedEvent> pendingUpdates)
    {
        if (!sessionId.HasValue || userId == Guid.Empty)
            return;

        var updatesSnapshot = pendingUpdates.ToArray();
        if (updatesSnapshot.Length == 0)
            return;

        await FlushPendingTokenUsageAsync(updatesSnapshot, sessionId.Value, userId);
    }

    private async Task EnsurePolicyMarkerMessagesAsync(Guid sessionId, Guid userId)
    {
        var existingMessages = await _chatHistoryService.GetMessagesAsync(sessionId, userId);

        await EnsureSinglePolicyMarkerMessageAsync(
            sessionId,
            userId,
            existingMessages,
            "Verification strategy: ",
            _effectiveVerificationStrategy);

        await EnsureSinglePolicyMarkerMessageAsync(
            sessionId,
            userId,
            existingMessages,
            "Execution policy: ",
            _effectiveExecutionPolicy);
    }

    private async Task EnsureSinglePolicyMarkerMessageAsync(
        Guid sessionId,
        Guid userId,
        IReadOnlyList<ChatMessageDto> existingMessages,
        string prefix,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var hasMarker = existingMessages.Any(m =>
            m.Role.Equals("system", StringComparison.OrdinalIgnoreCase) &&
            m.Content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (hasMarker)
            return;

        await _chatHistoryService.SaveMessageAsync(new SaveChatMessageRequest
        {
            SessionId = sessionId,
            Role = "system",
            Content = $"{prefix}{value}",
            MessageType = ChatMessageType.System,
            Metadata = "{}"
        }, userId);
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
                timestamp = update.Timestamp,
                effectiveVerificationStrategy = _effectiveVerificationStrategy,
                effectiveExecutionPolicy = _effectiveExecutionPolicy
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing conversation event");
        }
    }

    protected async Task WriteErrorEvent(string message, string? errorCode = null)
    {
        var payload = errorCode != null
            ? (object)new { error = message, errorCode }
            : new { error = message };
        await WriteEvent("error", payload);
    }

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
    /// <summary>Autonomous or human-in-the-middle (default: autonomous)</summary>
    public string Mode { get; set; } = ConversationModes.Autonomous;
}
