using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

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
    /// Get all chat sessions for a specific resource (Step or WebApp)
    /// </summary>
    /// <param name="resourceType">Type of resource (CustomStep or WebApp)</param>
    /// <param name="resourceId">ID of the resource</param>
    /// <returns>List of chat sessions</returns>
    [HttpGet("sessions")]
    public async Task<ActionResult<List<ChatSessionDto>>> GetSessions(
        [FromQuery] ChatResourceType resourceType,
        [FromQuery] Guid resourceId)
    {
        var userId = GetUserId();
        var sessions = await chatHistoryService.GetSessionsAsync(resourceType, resourceId, userId);
        return Ok(sessions);
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
}
