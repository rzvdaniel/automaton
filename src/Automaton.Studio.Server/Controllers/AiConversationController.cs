using Automaton.Mcp.Client;
using Automaton.Mcp.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<AiConversationController> _logger;

    public AiConversationController(
        IAutomatonMcpClient mcpClient,
        AiApiKeyService aiApiKeyService,
        ILogger<AiConversationController> logger)
    {
        _mcpClient = mcpClient;
        _aiApiKeyService = aiApiKeyService;
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

            // Get decrypted API key from database (NEVER from client)
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
                "Starting AI conversation for user {UserId}, provider {Provider}, model {Model}",
                userId, request.Provider, request.Model);

            // Build conversation request
            var conversationRequest = new ConversationRequest
            {
                ConversationId = request.ConversationId ?? Guid.NewGuid().ToString(),
                InitialMessage = request.Message,
                SystemPrompt = request.SystemPrompt ?? string.Empty,
                Provider = request.Provider,
                Model = request.Model,
                ApiKey = apiKey,
                ConversationHistory = request.History,
                Context = request.Context ?? new Dictionary<string, object>()
            };

            // Subscribe to conversation updates
            _mcpClient.ConversationUpdated += async (sender, e) =>
            {
                await WriteConversationEvent(e);
            };

            // Start conversation
            var result = await _mcpClient.StartConversationAsync(
                conversationRequest, 
                cancellationToken
            );

            // Write completion event
            if (result.IsSuccess)
            {
                await WriteEvent("done", new
                {
                    conversationId = result.ConversationId,
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
    /// System prompt (optional, built client-side if not provided)
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Conversation history (for continuing conversations)
    /// </summary>
    public List<ConversationMessage>? History { get; set; }

    /// <summary>
    /// Additional context (e.g., resourceType, resourceId, runnerId)
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }
}
