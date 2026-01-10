using System.Security.Claims;
using System.Text.Json;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for AI streaming conversations using Server-Sent Events (SSE)
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiStreamingController : ControllerBase
{
    private readonly AiStreamingService _streamingService;
    private readonly ILogger<AiStreamingController> _logger;

    public AiStreamingController(
        AiStreamingService streamingService,
        ILogger<AiStreamingController> logger)
    {
        _streamingService = streamingService;
        _logger = logger;
    }

    /// <summary>
    /// Stream an AI conversation using Server-Sent Events
    /// </summary>
    /// <remarks>
    /// This endpoint streams AI responses in real-time using SSE.
    /// The client should use EventSource or fetch with streaming to consume the response.
    ///
    /// Event types:
    /// - data: Regular chunk with content
    /// - done: Conversation complete
    /// - error: An error occurred
    /// </remarks>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    public async Task StreamConversation([FromBody] StreamConversationRequest request)
    {
        var userId = GetUserId();

        // Set SSE headers
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // Disable nginx buffering

        // Add user context
        request.Context ??= new Dictionary<string, object>();
        request.Context["userId"] = userId.ToString();

        try
        {
            await _streamingService.StreamConversationAsync(
                request,
                userId,
                async (chunk) =>
                {
                    var eventType = chunk.Type switch
                    {
                        StreamChunkType.Error => "error",
                        StreamChunkType.Complete => "done",
                        _ => "message"
                    };

                    var data = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await Response.WriteAsync($"event: {eventType}\n");
                    await Response.WriteAsync($"data: {data}\n\n");
                    await Response.Body.FlushAsync();
                },
                HttpContext.RequestAborted);

            // Final newline to close the stream properly
            await Response.WriteAsync("\n");
            await Response.Body.FlushAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client disconnected from streaming conversation {ConversationId}",
                request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming conversation {ConversationId}", request.ConversationId);

            var errorData = JsonSerializer.Serialize(new StreamChunk
            {
                Type = StreamChunkType.Error,
                Content = "An error occurred while processing your request."
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await Response.WriteAsync($"event: error\ndata: {errorData}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    /// <summary>
    /// Non-streaming conversation for simpler use cases
    /// </summary>
    [HttpPost("conversation")]
    public async Task<ActionResult<ConversationResponse>> StartConversation([FromBody] StreamConversationRequest request)
    {
        var userId = GetUserId();

        // Add user context
        request.Context ??= new Dictionary<string, object>();
        request.Context["userId"] = userId.ToString();

        var chunks = new List<StreamChunk>();
        string? errorMessage = null;

        await _streamingService.StreamConversationAsync(
            request,
            userId,
            async (chunk) =>
            {
                chunks.Add(chunk);
                if (chunk.Type == StreamChunkType.Error)
                {
                    errorMessage = chunk.Content;
                }
                await Task.CompletedTask;
            },
            HttpContext.RequestAborted);

        if (!string.IsNullOrEmpty(errorMessage))
        {
            return BadRequest(new { error = errorMessage });
        }

        // Aggregate all text content
        var textContent = string.Join("", chunks
            .Where(c => c.Type == StreamChunkType.Text)
            .Select(c => c.Content));

        // Get generated code if any
        var codeChunk = chunks.FirstOrDefault(c => c.Type == StreamChunkType.CodeGenerated);

        return Ok(new ConversationResponse
        {
            ConversationId = request.ConversationId,
            Content = textContent,
            GeneratedCode = codeChunk?.Code,
            CodeLanguage = codeChunk?.Language,
            ToolCalls = chunks
                .Where(c => c.Type == StreamChunkType.ToolCall && c.ToolCall != null)
                .Select(c => c.ToolCall!)
                .ToList(),
            ToolResults = chunks
                .Where(c => c.Type == StreamChunkType.ToolResult && c.ToolResult != null)
                .Select(c => c.ToolResult!)
                .ToList()
        });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user ID");
        }
        return userId;
    }
}

/// <summary>
/// Response for non-streaming conversation
/// </summary>
public class ConversationResponse
{
    public string ConversationId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? GeneratedCode { get; set; }
    public string? CodeLanguage { get; set; }
    public List<ToolCallInfo> ToolCalls { get; set; } = new();
    public List<ToolResultInfo> ToolResults { get; set; } = new();
}
