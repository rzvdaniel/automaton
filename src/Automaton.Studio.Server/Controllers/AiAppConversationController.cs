using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
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

    public AiAppConversationController(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        ISystemAiRateLimitService rateLimitService,
        AiProviderMetadataService providerMetadataService,
        AppMcpToolProvider toolProvider,
        IAppSystemPromptBuilder promptBuilder,
        IPromptClassifierService promptClassifier,
        AppInstructionInjector instructionInjector,
        ILogger<AiAppConversationController> logger)
        : base(mcpClient, aiApiKeyService, chatHistoryService, aiProvidersConfig,
               rateLimitService, providerMetadataService, logger)
    {
        _toolProvider         = toolProvider;
        _promptBuilder        = promptBuilder;
        _promptClassifier     = promptClassifier;
        _instructionInjector  = instructionInjector;
    }

    protected override ChatResourceType ResourceType => ChatResourceType.WebApp;

    protected override IMcpToolProvider? GetToolProvider() => _toolProvider;

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

        return prompt;
    }

    private async Task<ComplexityTier?> ResolveComplexityTierAsync(
        string userMessage, Guid sessionId, Guid userId, List<ConversationMessage>? history)
    {
        // Continuation: load sticky tier from session
        if (history?.Count > 0)
        {
            try
            {
                var session = await _chatHistoryService.GetSessionAsync(sessionId, userId);
                if (session?.ComplexityTier != null &&
                    Enum.TryParse<ComplexityTier>(session.ComplexityTier, ignoreCase: true, out var stored))
                {
                    _logger.LogInformation("Using sticky tier {Tier} for session {SessionId}", stored, sessionId);
                    return stored;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load sticky tier for session {SessionId}", sessionId);
            }
        }

        // New session: classify and persist (fire-and-forget persistence)
        try
        {
            var result = await _promptClassifier.ClassifyAsync(userMessage, userId.ToString());
            _logger.LogInformation(
                "Classified as {Tier} (confidence={Confidence}) for session {SessionId}",
                result.Tier, result.Confidence, sessionId);

            await _chatHistoryService.UpdateSessionComplexityTierAsync(sessionId, result.Tier.ToString(), userId);

            return result.Tier;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classification failed for session {SessionId}, using legacy prompt", sessionId);
            return null;
        }
    }
}

/// <summary>
/// Request to stream an AI conversation for a Web App.
/// </summary>
public class StreamAppConversationRequest : BaseStreamConversationRequest
{
    // App-specific: no Language field
}
