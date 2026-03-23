using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Services;
using Automaton.Mcp.SystemPrompts;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Automaton.Studio.Server.Services.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// AI conversation streaming for Web Apps via Server-Sent Events (SSE).
/// Applies complexity-tier classification to select the most suitable system prompt.
/// </summary>
[ApiController]
[Route("api/ai/app/conversation")]
[Authorize]
public class AiAppConversationController : AiConversationControllerBase
{
    private readonly AppMcpToolProvider _toolProvider;
    private readonly IAppSystemPromptBuilder _promptBuilder;
    private readonly IPromptClassifierService _promptClassifier;
    private readonly AppInstructionInjector _instructionInjector;
    private readonly AppVersionService _appVersionService;
    private readonly ISystemAiService _systemAiService;
    private readonly AppMemoryService _appMemoryService;

    public AiAppConversationController(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        ISystemAiRateLimitService rateLimitService,
        AiProviderMetadataService providerMetadataService,
        ITokenBudgetService tokenBudgetService,
        AppMcpToolProvider toolProvider,
        IAppSystemPromptBuilder promptBuilder,
        IPromptClassifierService promptClassifier,
        AppInstructionInjector instructionInjector,
        AppVersionService appVersionService,
        ISystemAiService systemAiService,
        AppMemoryService appMemoryService,
        ILogger<AiAppConversationController> logger)
        : base(mcpClient, aiApiKeyService, chatHistoryService, aiProvidersConfig,
               rateLimitService, providerMetadataService, tokenBudgetService, logger)
    {
        _toolProvider         = toolProvider;
        _promptBuilder        = promptBuilder;
        _promptClassifier     = promptClassifier;
        _instructionInjector  = instructionInjector;
        _appVersionService    = appVersionService;
        _systemAiService      = systemAiService;
        _appMemoryService     = appMemoryService;
    }

    protected override ChatResourceType ResourceType => ChatResourceType.WebApp;

    protected override IMcpToolProvider? GetToolProvider() => _toolProvider;

    /// <summary>
    /// Before the AI runs: check whether the app exists so we know to snapshot after.
    /// Returns the resourceId as the token, or null if skipped.
    /// </summary>
    protected override Task<object?> OnBeforeConversationAsync(
        BaseStreamConversationRequest request, Guid userId, Guid sessionId)
    {
        if (!request.ResourceId.HasValue) return Task.FromResult<object?>(null);
        if (!_appVersionService.AppHasBeenGenerated(request.ResourceId.Value, userId)) return Task.FromResult<object?>(null);

        // Pass (resourceId, sessionId) forward so OnAfterConversationAsync can snapshot post-generation
        return Task.FromResult<object?>((request.ResourceId.Value, sessionId));
    }

    /// <summary>
    /// After the AI completes: snapshot the new state and summarize it.
    /// Snapshot is taken post-generation so restoring Version N gives the state described by its summary.
    /// </summary>
    protected override async Task OnAfterConversationAsync(
        BaseStreamConversationRequest request, Guid userId, ConversationResult result, object? token)
    {
        if (token is not (Guid resourceId, Guid sessionId)) return;

        try
        {
            var snapshot = await _appVersionService.CreateSnapshotAsync(
                resourceId,
                userId,
                AppVersionTrigger.NewGeneration,
                aiSessionId: sessionId);

            _logger.LogInformation("Created post-generation snapshot {SnapshotId} for app {AppId}", snapshot.Id, resourceId);

            if (!_systemAiService.IsAvailable) return;

            var lastAssistantMessage = result.Messages
                .LastOrDefault(m => m.Role == "assistant")?.Content ?? string.Empty;

            if (string.IsNullOrWhiteSpace(lastAssistantMessage)) return;

            var summary = await _systemAiService.SummarizeGenerationAsync(
                request.Message,
                lastAssistantMessage,
                userId.ToString());

            await _appVersionService.UpdateSummaryAsync(snapshot.Id, summary);
            _logger.LogInformation("Updated version summary for snapshot {SnapshotId}", snapshot.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create/summarize post-generation snapshot for app {AppId}", resourceId);
        }
    }

    /// <summary>
    /// Stream an AI conversation for Web App generation/editing.
    /// </summary>
    [HttpPost("stream")]
    public Task StreamConversation(
        [FromBody] StreamAppConversationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteStreamAsync(request, cancellationToken);

    protected override async Task<string> BuildSystemPromptAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        List<ConversationMessage>? history)
    {
        var tier       = await ResolveComplexityTierAsync(request.Message, sessionId, userId, history);
        _resolvedTier  = tier;
        var basePrompt = _promptBuilder.BuildSystemPrompt(tier);

        // Inject user instructions from .instructions/ folder (1000 token budget)
        string? instructionBlock = null;
        if (request.ResourceId.HasValue)
        {
            try
            {
                instructionBlock = await _instructionInjector.BuildInjectionBlockAsync(userId, request.ResourceId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inject instructions for app {AppId}", request.ResourceId);
            }
        }

        var prompt = instructionBlock is not null
            ? $"{basePrompt}\n\n{instructionBlock}"
            : basePrompt;

        if (history?.Count > 0)
        {
            _logger.LogInformation(
                "Adding continuation context for session {SessionId} ({Count} messages)", sessionId, history.Count);
            return $"{prompt}\n\nIMPORTANT: This is a continuation of a previous conversation. Review the conversation history provided above and continue naturally from where we left off. Do not restart, regenerate, or repeat work that has already been completed unless explicitly asked to do so.";
        }

        // New session for an existing app — inject memory so the AI knows what has already been built
        if (request.ResourceId.HasValue)
        {
            try
            {
                var memoryBlock = await _appMemoryService.BuildMemoryBlockAsync(
                    request.ResourceId.Value, sessionId, userId);

                if (memoryBlock is not null)
                {
                    _logger.LogInformation(
                        "Injecting app memory block for new session on existing app {AppId}", request.ResourceId.Value);
                    return $"{prompt}\n\n{memoryBlock}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build app memory block for app {AppId}", request.ResourceId.Value);
            }
        }

        return prompt;
    }

    private async Task<ComplexityTier?> ResolveComplexityTierAsync(
        string userMessage, Guid sessionId, Guid userId, List<ConversationMessage>? history)
    {
        // Load stored tier (may be null for new sessions)
        ComplexityTier? storedTier = null;
        if (history?.Count > 0)
        {
            try
            {
                var session = await _chatHistoryService.GetSessionAsync(sessionId, userId);
                if (session?.ComplexityTier != null &&
                    Enum.TryParse<ComplexityTier>(session.ComplexityTier, ignoreCase: true, out var parsed))
                {
                    storedTier = parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load stored tier for session {SessionId}", sessionId);
            }
        }

        // Classify the current request
        ComplexityTier? newTier = null;
        try
        {
            var result = await _promptClassifier.ClassifyAsync(userMessage, userId.ToString());
            newTier = result.Tier;
            _logger.LogInformation(
                "Classified as {Tier} (confidence={Confidence}) for session {SessionId}",
                result.Tier, result.Confidence, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classification failed for session {SessionId}", sessionId);
        }

        // Tier can only upgrade — take the higher of stored vs new
        var resolved = Max(storedTier, newTier);
        if (resolved == null)
            return null;

        if (resolved != storedTier)
        {
            _logger.LogInformation(
                "Tier upgraded from {StoredTier} to {NewTier} for session {SessionId}",
                storedTier?.ToString() ?? "none", resolved, sessionId);
            await _chatHistoryService.UpdateSessionComplexityTierAsync(sessionId, resolved.ToString()!, userId);
        }

        return resolved;
    }

    private static ComplexityTier? Max(ComplexityTier? a, ComplexityTier? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return (ComplexityTier)Math.Max((int)a, (int)b);
    }
}

/// <summary>
/// Request to stream an AI conversation for a Web App.
/// </summary>
public class StreamAppConversationRequest : BaseStreamConversationRequest
{
    // App-specific: no Language field
}
