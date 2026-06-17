using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Automaton.Api.Controllers;

/// <summary>
/// Controller for managing AI chat history (sessions and messages)
/// </summary>
public class AiChatHistoryController : BaseController
{
    private readonly IAiChatHistoryService chatHistoryService;

    public AiChatHistoryController(IAiChatHistoryService chatHistoryService)
    {
        this.chatHistoryService = chatHistoryService;
    }

    /// <summary>
    /// Get paginated chat sessions for a specific resource (Step or WebApp)
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<PagedResult<ChatSessionDto>>> GetSessions(
        [FromQuery] ChatResourceType resourceType,
        [FromQuery] Guid resourceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.GetSessionsAsync(resourceType, resourceId, userId, page, pageSize, search);
        return Ok(result);
    }

    /// <summary>
    /// Export all chat sessions and messages for a specific resource as JSON.
    /// </summary>
    [HttpGet("sessions/export")]
    public async Task<IActionResult> ExportSessions(
        [FromQuery] ChatResourceType resourceType,
        [FromQuery] Guid resourceId)
    {
        var userId = GetUserId();
        var export = await chatHistoryService.ExportResourceSessionsAsync(resourceType, resourceId, userId);

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var fileName = $"ai-chat-sessions-{resourceType.ToString().ToLowerInvariant()}-{resourceId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    /// <summary>
    /// Get a specific chat session by ID
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Chat session details</returns>
    [HttpGet("sessions/{sessionId}")]
    public async Task<ActionResult<ChatSessionDto>> GetSession(Guid sessionId)
    {
        var userId = GetUserId();
        var session = await chatHistoryService.GetSessionAsync(sessionId, userId);

        if (session == null)
        {
            return NotFound();
        }

        return Ok(session);
    }

    /// <summary>
    /// Get messages for a specific session
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>List of chat messages</returns>
    [HttpGet("sessions/{sessionId}/messages")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(Guid sessionId)
    {
        var userId = GetUserId();
        var messages = await chatHistoryService.GetMessagesAsync(sessionId, userId);
        return Ok(messages);
    }

    /// <summary>
    /// Get session with all its messages
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Session with messages</returns>
    [HttpGet("sessions/{sessionId}/full")]
    public async Task<ActionResult<ChatSessionWithMessagesDto>> GetSessionWithMessages(Guid sessionId)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.GetSessionWithMessagesAsync(sessionId, userId);

        if (result == null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// Get or create the active session for a resource
    /// </summary>
    /// <param name="resourceType">Type of resource</param>
    /// <param name="resourceId">ID of the resource</param>
    /// <param name="provider">Optional AI provider</param>
    /// <param name="model">Optional AI model</param>
    /// <returns>Active chat session</returns>
    [HttpGet("sessions/active")]
    public async Task<ActionResult<ChatSessionDto>> GetOrCreateActiveSession(
        [FromQuery] ChatResourceType resourceType,
        [FromQuery] Guid resourceId,
        [FromQuery] string? provider = null,
        [FromQuery] string? model = null)
    {
        var userId = GetUserId();
        var session = await chatHistoryService.GetOrCreateActiveSessionAsync(
            resourceType, resourceId, userId, provider, model);
        return Ok(session);
    }

    /// <summary>
    /// Create a new chat session
    /// </summary>
    /// <param name="request">Session creation request</param>
    /// <returns>Created session</returns>
    [HttpPost("sessions")]
    public async Task<ActionResult<ChatSessionDto>> CreateSession([FromBody] CreateChatSessionRequest request)
    {
        var userId = GetUserId();
        var session = await chatHistoryService.CreateSessionAsync(request, userId);
        return CreatedAtAction(nameof(GetSession), new { sessionId = session.Id }, session);
    }

    /// <summary>
    /// Set a session as the active session
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Success status</returns>
    [HttpPut("sessions/{sessionId}/activate")]
    public async Task<IActionResult> SetActiveSession(Guid sessionId)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.SetActiveSessionAsync(sessionId, userId);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Update session title
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="request">Update request with new title</param>
    /// <returns>Success status</returns>
    [HttpPut("sessions/{sessionId}/title")]
    public async Task<IActionResult> UpdateSessionTitle(Guid sessionId, [FromBody] UpdateSessionTitleRequest request)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.UpdateSessionTitleAsync(sessionId, request.Title, userId);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Success status</returns>
    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.DeleteSessionAsync(sessionId, userId);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Save a single chat message to a session
    /// </summary>
    /// <param name="request">Message save request</param>
    /// <returns>Saved message</returns>
    [HttpPost("messages")]
    public async Task<ActionResult<ChatMessageDto>> SaveMessage([FromBody] SaveChatMessageRequest request)
    {
        var userId = GetUserId();
        var message = await chatHistoryService.SaveMessageAsync(request, userId);
        return Ok(message);
    }

    /// <summary>
    /// Save multiple chat messages to a session (batch operation)
    /// </summary>
    /// <param name="request">Batch message save request</param>
    /// <returns>List of saved messages</returns>
    [HttpPost("messages/batch")]
    public async Task<ActionResult<List<ChatMessageDto>>> SaveMessages([FromBody] SaveChatMessagesRequest request)
    {
        var userId = GetUserId();
        var messages = await chatHistoryService.SaveMessagesAsync(request, userId);
        return Ok(messages);
    }

    /// <summary>
    /// Clear all messages from a session (start new conversation in same session)
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Success status</returns>
    [HttpDelete("sessions/{sessionId}/messages")]
    public async Task<IActionResult> ClearSessionMessages(Guid sessionId)
    {
        var userId = GetUserId();
        var result = await chatHistoryService.ClearSessionMessagesAsync(sessionId, userId);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// Get aggregate AI chat statistics for a specific app (WebApp)
    /// </summary>
    /// <param name="appId">App ID</param>
    /// <returns>App chat statistics including total prompts, tokens, and session count</returns>
    [HttpGet("apps/{appId}/stats")]
    public async Task<ActionResult<AppChatStatsDto>> GetAppChatStats(Guid appId)
    {
        var userId = GetUserId();
        var stats = await chatHistoryService.GetAppChatStatsAsync(appId, userId);

        if (stats == null)
        {
            return NotFound();
        }

        return Ok(stats);
    }
}
