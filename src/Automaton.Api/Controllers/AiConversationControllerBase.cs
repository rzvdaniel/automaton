using Automaton.Ai.Models;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Mcp.Client;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Services;
using Automaton.Mcp.SystemPrompts;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Automaton.Api.Controllers;

/// <summary>
/// Shared SSE streaming, session management, and history persistence logic
/// for all AI conversation controllers.
/// </summary>
[Authorize]
public abstract class AiConversationControllerBase : BaseController
{
    protected readonly IAutomatonMcpClient _mcpClient;
    protected readonly AiConversationHistoryPersistenceService _historyPersistenceService;
    protected readonly AiConversationProviderResolver _providerResolver;
    protected readonly ISystemAiRateLimitService _rateLimitService;
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
        AiConversationHistoryPersistenceService historyPersistenceService,
        AiConversationProviderResolver providerResolver,
        ISystemAiRateLimitService rateLimitService,
        IAppSystemPromptBuilder appPromptBuilder,
        ILogger logger)
    {
        _mcpClient = mcpClient;
        _historyPersistenceService = historyPersistenceService;
        _providerResolver = providerResolver;
        _rateLimitService = rateLimitService;
        _appPromptBuilder = appPromptBuilder;
        _logger = logger;
    }

    // ── Subclass hooks ────────────────────────────────────────────────────

    /// <summary>Build the system prompt parts for the given request and session context.</summary>
    protected abstract Task<SystemPromptParts> BuildSystemPromptAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        List<ConversationMessage>? history,
        CancellationToken cancellationToken);

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

            var providerResolution = await _providerResolver.ResolveAsync(userId, request.Provider, request.Model);
            if (providerResolution.Configuration is null)
            {
                await WriteErrorEvent(providerResolution.ErrorMessage!);
                return;
            }

            var providerConfiguration = providerResolution.Configuration;
            isSystemKey = providerConfiguration.IsSystemKey;
            var provider = providerConfiguration.Provider;
            var model = providerConfiguration.Model;
            var aiConfig = providerConfiguration.Settings;

            // 2. Resolve session
            sessionId = await _historyPersistenceService.ResolveSessionAsync(
                request.SessionId,
                request.ResourceId,
                ResourceType,
                userId,
                provider,
                model);
            var resolvedSessionId = sessionId.Value;

            var preparation = await PrepareConversationAsync(
                request,
                userId,
                resolvedSessionId,
                aiConfig,
                cancellationToken);

            // 7. Subscribe to events and run conversation
            eventHandler = (_, e) =>
            {
                _ = WriteConversationEvent(e);
                pendingUpdates.Enqueue(e);
            };
            _mcpClient.ConversationUpdated += eventHandler;

            // 8. Run conversation
            var conversationRequest = BuildConversationRequest(request, userId, providerConfiguration, preparation);

            var result = await _mcpClient.StartConversationAsync(conversationRequest, cancellationToken);

            var updatesSnapshot = new List<ConversationUpdatedEvent>(pendingUpdates.ToArray());
            await HandleConversationResultAsync(
                request,
                result,
                updatesSnapshot,
                resolvedSessionId,
                userId,
                model,
                isSystemKey,
                preparation.ConversationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("AI conversation stream cancelled");

            // Client cancelled/closed stream — still persist consumed token usage.
            await _historyPersistenceService.FlushPendingTokenUsageIfPossibleAsync(sessionId, userId, pendingUpdates);

            await WriteEvent("cancelled", new { message = "Stream cancelled by client" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI conversation stream");

            // Persist token usage for unexpected failures as best-effort accounting.
            await _historyPersistenceService.FlushPendingTokenUsageIfPossibleAsync(sessionId, userId, pendingUpdates);

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

    private async Task<ConversationPreparation> PrepareConversationAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        Automaton.Api.Config.AiProvidersConfig aiConfig,
        CancellationToken cancellationToken)
    {
        var history = await _historyPersistenceService.LoadConversationHistoryAsync(sessionId, userId);
        var compactedHistory = ConversationHistoryCompactor.Compact(
            history,
            aiConfig.MaxConversationHistoryMessages,
            aiConfig.HistoryCompaction);

        var promptParts = await BuildSystemPromptAsync(request, userId, sessionId, compactedHistory, cancellationToken);
        var mode = ConversationModeFactory.From(request.Mode);
        var promptPartsWithMode = promptParts with
        {
            BasePrompt = promptParts.BasePrompt + mode.SystemPromptSuffix
        };

        _mcpClient.SetToolProvider(GetToolProvider());
        var conversationToken = await OnBeforeConversationAsync(request, userId, sessionId);
        await _historyPersistenceService.EnsurePolicyMarkerMessagesAsync(
            sessionId,
            userId,
            _effectiveVerificationStrategy,
            _effectiveExecutionPolicy);
        await _historyPersistenceService.SaveUserMessageAsync(sessionId, request.Message, userId);

        return new ConversationPreparation(compactedHistory, promptPartsWithMode, mode, conversationToken);
    }

    private ConversationRequest BuildConversationRequest(
        BaseStreamConversationRequest request,
        Guid userId,
        AiConversationProviderConfiguration providerConfiguration,
        ConversationPreparation preparation)
    {
        var aiConfig = providerConfiguration.Settings;
        var mode = preparation.Mode;
        return new ConversationRequest
        {
            ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
            InitialMessage = request.Message,
            SystemPrompt = preparation.SystemPromptParts.Full,
            SystemPromptParts = preparation.SystemPromptParts,
            Provider = providerConfiguration.Provider,
            Model = providerConfiguration.Model,
            ApiKey = providerConfiguration.ApiKey,
            ConversationHistory = preparation.History,
            Context = BuildContextDictionary(request, userId),
            MaxIterations = mode.Name == ConversationModes.Autonomous
                ? IterationLimits.ResolveForAutonomous(
                    globalOverride: aiConfig.MaxIterations,
                    autonomousOverride: aiConfig.IterationLimits.Autonomous)
                : IterationLimits.Resolve(
                    _resolvedTier,
                    globalOverride: aiConfig.MaxIterations,
                    tierOverride: aiConfig.IterationLimits.For(_resolvedTier)),
            MaxConsecutiveFailures = aiConfig.MaxConsecutiveFailures,
            MaxTokens = providerConfiguration.MaxTokens,
            MaxRateLimitRetries = aiConfig.RateLimitRetry.MaxRetries,
            RateLimitRetryInitialDelaySeconds = aiConfig.RateLimitRetry.InitialDelaySeconds,
            StreamingTimeoutMinutes = aiConfig.Streaming.TimeoutMinutes,
            Mode = request.Mode,
            ExtraBodyJson = providerConfiguration.ExtraBodyJson,
            SupportsTools = providerConfiguration.SupportsTools
        };
    }

    private sealed record ConversationPreparation(
        List<ConversationMessage>? History,
        SystemPromptParts SystemPromptParts,
        IConversationMode Mode,
        object? ConversationToken);

    private async Task HandleConversationResultAsync(
        BaseStreamConversationRequest request,
        ConversationResult result,
        IReadOnlyList<ConversationUpdatedEvent> updates,
        Guid sessionId,
        Guid userId,
        string model,
        bool isSystemKey,
        object? conversationToken)
    {
        if (!result.IsSuccess)
        {
            await HandleFailedConversationAsync(result, updates, sessionId, userId, isSystemKey);
            return;
        }

        await PersistSuccessfulConversationAsync(
            request,
            result,
            updates,
            sessionId,
            userId,
            model,
            conversationToken);
    }

    private async Task HandleFailedConversationAsync(
        ConversationResult result,
        IReadOnlyList<ConversationUpdatedEvent> updates,
        Guid sessionId,
        Guid userId,
        bool isSystemKey)
    {
        await _historyPersistenceService.FlushPendingTokenUsageAsync(updates, sessionId, userId);
        var errorMessage = result.ErrorMessage ?? "Conversation failed";
        if (IsRateLimitError(errorMessage))
        {
            await WriteErrorEvent(RateLimitMessage(isSystemKey), "RATE_LIMIT");
            return;
        }

        await WriteErrorEvent(errorMessage);
    }

    private async Task PersistSuccessfulConversationAsync(
        BaseStreamConversationRequest request,
        ConversationResult result,
        IReadOnlyList<ConversationUpdatedEvent> updates,
        Guid sessionId,
        Guid userId,
        string model,
        object? conversationToken)
    {
        var savedToolCallIds = new HashSet<string>();
        foreach (var update in updates)
            await _historyPersistenceService.SaveConversationUpdateAsync(update, sessionId, userId, savedToolCallIds);

        if (result.ApiCallCount > 0)
            await _historyPersistenceService.IncrementApiCallsAsync(sessionId, userId, result.ApiCallCount);

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
