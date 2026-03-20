using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services.Interfaces;

/// <summary>
/// Service for managing AI chat history (sessions and messages)
/// </summary>
public interface IAiChatHistoryService
{
    /// <summary>
    /// Get paginated chat sessions for a specific resource (Step or WebApp)
    /// </summary>
    Task<PagedResult<ChatSessionDto>> GetSessionsAsync(
        ChatResourceType resourceType, Guid resourceId, Guid userId,
        int page = 1, int pageSize = 20, string? search = null);

    /// <summary>
    /// Get a specific chat session by ID
    /// </summary>
    Task<ChatSessionDto?> GetSessionAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Get messages for a specific session
    /// </summary>
    Task<List<ChatMessageDto>> GetMessagesAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Get session with all its messages
    /// </summary>
    Task<ChatSessionWithMessagesDto?> GetSessionWithMessagesAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Get the active session for a resource, or create one if none exists
    /// </summary>
    Task<ChatSessionDto> GetOrCreateActiveSessionAsync(ChatResourceType resourceType, Guid resourceId, Guid userId, string? provider = null, string? model = null);

    /// <summary>
    /// Create a new chat session
    /// </summary>
    Task<ChatSessionDto> CreateSessionAsync(CreateChatSessionRequest request, Guid userId);

    /// <summary>
    /// Set a session as the active session (deactivates other sessions for the same resource)
    /// </summary>
    Task<bool> SetActiveSessionAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Update session title
    /// </summary>
    Task<bool> UpdateSessionTitleAsync(Guid sessionId, string title, Guid userId);

    /// <summary>
    /// Delete a chat session and all its messages
    /// </summary>
    Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Save a single chat message to a session
    /// </summary>
    Task<ChatMessageDto> SaveMessageAsync(SaveChatMessageRequest request, Guid userId);

    /// <summary>
    /// Save multiple chat messages to a session (batch operation)
    /// </summary>
    Task<List<ChatMessageDto>> SaveMessagesAsync(SaveChatMessagesRequest request, Guid userId);

    /// <summary>
    /// Clear all messages from a session (start new conversation in same session)
    /// </summary>
    Task<bool> ClearSessionMessagesAsync(Guid sessionId, Guid userId);

    /// <summary>
    /// Get aggregate AI chat statistics for a specific app (WebApp)
    /// </summary>
    Task<AppChatStatsDto?> GetAppChatStatsAsync(Guid appId, Guid userId);

    /// <summary>
    /// Persist the complexity tier for a session (sticky — only upgrades, never downgrades).
    /// </summary>
    Task UpdateSessionComplexityTierAsync(Guid sessionId, string tier, Guid userId);

    /// <summary>
    /// Add input/output token counts to a session's running totals without saving a message.
    /// Used for AI turns that consist purely of tool calls (no text content to persist).
    /// </summary>
    Task AccumulateTokensAsync(Guid sessionId, Guid userId, int? inputTokens, int? outputTokens);
}
