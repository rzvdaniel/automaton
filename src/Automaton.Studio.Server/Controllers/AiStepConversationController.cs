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
/// AI conversation streaming for Custom Steps via Server-Sent Events (SSE).
/// Selects the step system prompt based on the requested programming language.
/// </summary>
[ApiController]
[Route("api/ai/step/conversation")]
[Authorize]
public class AiStepConversationController : AiConversationControllerBase
{
    private readonly StepMcpToolProvider _toolProvider;
    private readonly IStepSystemPromptBuilder _promptBuilder;

    public AiStepConversationController(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        IAiChatHistoryService chatHistoryService,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        ISystemAiRateLimitService rateLimitService,
        AiProviderMetadataService providerMetadataService,
        StepMcpToolProvider toolProvider,
        IStepSystemPromptBuilder promptBuilder,
        ILogger<AiStepConversationController> logger)
        : base(mcpClient, aiApiKeyService, chatHistoryService, aiProvidersConfig,
               rateLimitService, providerMetadataService, logger)
    {
        _toolProvider = toolProvider;
        _promptBuilder = promptBuilder;
    }

    protected override ChatResourceType ResourceType => ChatResourceType.CustomStep;

    protected override IMcpToolProvider? GetToolProvider() => _toolProvider;

    /// <summary>
    /// Stream an AI conversation for Custom Step code generation/editing.
    /// </summary>
    [HttpPost("stream")]
    public Task StreamConversation(
        [FromBody] StreamStepConversationRequest request,
        CancellationToken cancellationToken) =>
        ExecuteStreamAsync(request, cancellationToken);

    protected override Task<string> BuildSystemPromptAsync(
        BaseStreamConversationRequest request,
        Guid userId,
        Guid sessionId,
        List<ConversationMessage>? history)
    {
        var stepRequest = (StreamStepConversationRequest)request;
        var basePrompt = _promptBuilder.BuildSystemPrompt(stepRequest.Language ?? "Python");

        if (history?.Count > 0)
        {
            _logger.LogInformation(
                "Adding continuation context for step session {SessionId} ({Count} messages)", sessionId, history.Count);
            return Task.FromResult(
                $"{basePrompt}\n\nIMPORTANT: This is a continuation of a previous conversation. Review the conversation history provided above and continue naturally from where we left off. Do not restart, regenerate, or repeat work that has already been completed unless explicitly asked to do so.");
        }

        return Task.FromResult(basePrompt);
    }

    protected override Dictionary<string, object> BuildContextDictionary(
        BaseStreamConversationRequest request, Guid userId)
    {
        var context = base.BuildContextDictionary(request, userId);
        var stepRequest = (StreamStepConversationRequest)request;
        if (stepRequest.Language != null)
            context["language"] = stepRequest.Language;
        return context;
    }
}

/// <summary>
/// Request to stream an AI conversation for a Custom Step.
/// </summary>
public class StreamStepConversationRequest : BaseStreamConversationRequest
{
    /// <summary>
    /// Programming language for code generation (e.g., "Python", "PowerShell", "JavaScript").
    /// </summary>
    public string? Language { get; set; }
}
