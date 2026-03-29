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

            // Downgrade model if user has exceeded their monthly token allowance
            model = await _tokenBudgetService.GetEffectiveModelAsync(userId, provider, model);

            var aiConfig = await _settingsService.GetEffectiveConfigAsync();

            var modelMaxTokens = await _providerMetadataService.GetModelMaxTokensAsync(provider, model);

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

            // 4. Build system prompt parts (delegated to subclass) + append mode-specific instructions
            var promptParts = await BuildSystemPromptAsync(request, userId, sessionId, history);
            var mode = ConversationModeFactory.From(request.Mode);
            var modePromptSuffix = mode.SystemPromptSuffix;
            // Append mode suffix to the base prompt so it stays within the cached block
            var partsWithMode = promptParts with { BasePrompt = promptParts.BasePrompt + modePromptSuffix };

            // 5. Wire tool provider
            _mcpClient.SetToolProvider(GetToolProvider());

            // 6. Pre-conversation hook (e.g. create version snapshot)
            var conversationToken = await OnBeforeConversationAsync(request, userId, sessionId);

            // 7. Subscribe to events and run conversation
            var pendingUpdates = new List<ConversationUpdatedEvent>();

            EventHandler<ConversationUpdatedEvent> eventHandler = (_, e) =>
            {
                _ = WriteConversationEvent(e);
                lock (pendingUpdates) { pendingUpdates.Add(e); }
            };
            _mcpClient.ConversationUpdated += eventHandler;

            // 8a. Autonomous planning phase — run planning model first if one is configured.
            //     Events flow through the existing eventHandler, so the user sees the plan being written in real-time.
            if (mode.Name == ConversationModes.Autonomous)
            {
                var planWasGenerated = await RunAutonomousPlanningPhaseAsync(
                    request, userId, aiConfig, promptParts.BasePrompt, cancellationToken);

                if (planWasGenerated)
                {
                    // Tell the implementation model to read and follow the plan that was just written.
                    partsWithMode = partsWithMode with
                    {
                        BasePrompt = partsWithMode.BasePrompt +
                            "\n\n## FOLLOW THE IMPLEMENTATION PLAN\n" +
                            "A detailed implementation plan has already been prepared for this task. " +
                            "Call `read_file` on `.plans/IMPLEMENTATION.md` as your FIRST action. " +
                            "Implement every file, structure, and decision specified in the plan exactly. " +
                            "Do NOT call `propose_plan` — the plan is already written."
                    };
                }
            }

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
                ConversationHistory = history,
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

                if (result.ApiCallCount > 0)
                    await _chatHistoryService.IncrementApiCallsAsync(sessionId, userId, result.ApiCallCount);

                // 8b. Post-conversation hook (e.g. update version summary)
                await OnAfterConversationAsync(request, userId, result, conversationToken);

                await WriteEvent("done", new
                {
                    conversationId = result.ConversationId,
                    sessionId,
                    model,
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
            await WriteErrorEvent(ex.Message);
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
        await WriteEvent("error", new { error = "Something went wrong. Please try again in a moment." });

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

    // ── Autonomous planning phase ─────────────────────────────────────────

    /// <summary>
    /// Runs a short planning conversation using the globally configured planning model.
    /// The model calls <c>propose_plan</c> once to write <c>.plans/IMPLEMENTATION.md</c>,
    /// then stops. Events are forwarded through the existing ConversationUpdated subscription
    /// so the user sees the plan in real-time.
    /// </summary>
    /// <returns>
    /// <c>true</c> if a plan file was successfully written; <c>false</c> if there is no
    /// planning model configured, the API key is missing, or the planning phase fails.
    /// </returns>
    private async Task<bool> RunAutonomousPlanningPhaseAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        AiProvidersConfig aiConfig,
        string baseSystemPrompt,
        CancellationToken cancellationToken)
    {
        var planningModel = await _providerMetadataService.GetGlobalPlanningModelAsync();
        if (planningModel is null)
        {
            _logger.LogDebug("No planning model configured — skipping autonomous planning phase");
            return false;
        }

        var planningApiKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(userId, planningModel.Value.ProviderName);
        if (string.IsNullOrEmpty(planningApiKey))
        {
            _logger.LogWarning(
                "No API key for planning provider '{Provider}' — skipping planning phase",
                planningModel.Value.ProviderName);
            return false;
        }

        var planningSystemPrompt = BuildPlanningPhaseSystemPrompt(baseSystemPrompt);
        var planningModelMaxTokens = await _providerMetadataService.GetModelMaxTokensAsync(
            planningModel.Value.ProviderName, planningModel.Value.ModelId);

        var planningRequest = new ConversationRequest
        {
            ConversationId = Guid.NewGuid().ToString(),
            InitialMessage = request.Message,
            SystemPrompt = planningSystemPrompt,
            SystemPromptParts = new SystemPromptParts(planningSystemPrompt),
            Provider = planningModel.Value.ProviderName,
            Model = planningModel.Value.ModelId,
            ApiKey = planningApiKey,
            ConversationHistory = null,
            Context = new Dictionary<string, object>
            {
                ["planningPhase"] = "autonomous",
                ["contextId"]     = request.ResourceId?.ToString() ?? string.Empty,
                ["resourceId"]    = (object)(request.ResourceId ?? Guid.Empty),
                ["userId"]        = userId,
            },
            MaxIterations = IterationLimits.ResolveForPlanning(aiConfig.IterationLimits.Planning),
            MaxConsecutiveFailures = aiConfig.MaxConsecutiveFailures,
            MaxTokens = planningModelMaxTokens,
            MaxRateLimitRetries = aiConfig.RateLimitRetry.MaxRetries,
            RateLimitRetryInitialDelaySeconds = aiConfig.RateLimitRetry.InitialDelaySeconds,
            StreamingTimeoutMinutes = aiConfig.Streaming.TimeoutMinutes,
            Mode = ConversationModes.Autonomous,
        };

        _logger.LogInformation(
            "Starting autonomous planning phase: provider={Provider}, model={Model}",
            planningModel.Value.ProviderName, planningModel.Value.ModelId);

        var planResult = await _mcpClient.StartConversationAsync(planningRequest, cancellationToken);

        if (!planResult.IsSuccess)
        {
            _logger.LogWarning(
                "Autonomous planning phase failed: {Error} — proceeding without plan",
                planResult.ErrorMessage);
            return false;
        }

        _logger.LogInformation("Autonomous planning phase completed successfully");
        return true;
    }

    /// <summary>
    /// Builds a system prompt for the planning phase by appending detailed planning
    /// instructions to the app-specific base prompt.
    /// </summary>
    private static string BuildPlanningPhaseSystemPrompt(string baseSystemPrompt) =>
        baseSystemPrompt + "\n\n" +
        "## PLANNING PHASE — YOUR ONLY JOB\n\n" +
        "You are in the PLANNING PHASE. A separate AI agent will implement what you plan.\n\n" +
        "Your task:\n" +
        "1. Analyse the user's request thoroughly\n" +
        "2. Call `propose_plan` ONCE with a comprehensive, highly detailed implementation plan\n" +
        "3. After `propose_plan` returns, write ONE brief confirmation sentence\n" +
        "4. STOP — do not call any further tools, do not write any files\n\n" +
        "---\n\n" +
        "## PLAN QUALITY REQUIREMENTS\n\n" +
        "The plan must be detailed enough that a separate AI agent " +
        "(with no additional context) can implement the task perfectly from it alone.\n\n" +
        "Include:\n" +
        "- **Every file** to be created or modified, with exact relative paths\n" +
        "- **Directory structure**: complete file tree of what will exist after implementation\n" +
        "- **CSS design**: exact colors (hex/rgb), class names (use BEM-like naming: `.card__title`, `.btn--danger`), layout approach (grid/flex), responsive breakpoints\n" +
        "- **JavaScript/TypeScript architecture**: module responsibilities split by role (state / api / rendering / events), key function signatures, data flow\n" +
        "- **HTML structure**: semantic elements (`<header>`, `<nav>`, `<main>`, `<dialog>`, etc.), data attributes used as JS hooks (`data-action`, `data-id`)\n" +
        "- **Data structures**: interfaces, types, schema definitions with exact field names and types\n" +
        "- **Component hierarchy**: parent-child relationships, props, events\n" +
        "- **API contracts**: endpoint URLs, HTTP methods, request/response shapes\n" +
        "- **Import/load order**: which files import which, in what order they must load\n" +
        "- **Technical decisions**: chosen libraries, patterns, and the reason for each\n" +
        "- **Known pitfalls**: specific edge cases to handle (e.g. 'dispose event listeners on unmount')\n" +
        "- **Exact content**: for small configuration or boilerplate files, include the full file content\n\n" +
        "CRITICAL: The more detail you provide, the better the implementation will be.";
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
    public string Mode { get; set; } = ConversationModes.Agent;
}
