using Automaton.Studio.Server.Enums;

#nullable disable

namespace Automaton.Studio.Server.Models;

/// <summary>
/// DTO for AI chat session information
/// </summary>
public class ChatSessionDto
{
    public Guid Id { get; set; }
    public ChatResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string Title { get; set; }
    public string Provider { get; set; }
    public string Model { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    
    // Usage metrics
    public int TotalPromptsSubmitted { get; set; }
    public int TotalApiCalls { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public int TotalCacheReadTokens { get; set; }
    public int TotalCacheCreationTokens { get; set; }

    /// <summary>
    /// The detected complexity tier for this session (Simple, Medium, Large, Enterprise).
    /// </summary>
    public string? ComplexityTier { get; set; }
}

/// <summary>
/// DTO for AI chat message information
/// </summary>
public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public ChatMessageType MessageType { get; set; }
    public string Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Token usage
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public string? Model { get; set; }
}

/// <summary>
/// Request to create a new chat session
/// </summary>
public class CreateChatSessionRequest
{
    public ChatResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string Title { get; set; }
    public string Provider { get; set; }
    public string Model { get; set; }
}

/// <summary>
/// Request to save a chat message
/// </summary>
public class SaveChatMessageRequest
{
    public Guid SessionId { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public ChatMessageType MessageType { get; set; }
    public string Metadata { get; set; }
    
    // Token usage
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Model { get; set; }

    // Anthropic prompt cache stats
    public int? CacheReadTokens { get; set; }
    public int? CacheCreationTokens { get; set; }
}

/// <summary>
/// Request to save multiple chat messages (batch operation)
/// </summary>
public class SaveChatMessagesRequest
{
    public Guid SessionId { get; set; }
    public List<ChatMessageItem> Messages { get; set; }
}

/// <summary>
/// Individual message item for batch save
/// </summary>
public class ChatMessageItem
{
    public string Role { get; set; }
    public string Content { get; set; }
    public ChatMessageType MessageType { get; set; }
    public string Metadata { get; set; }
}

/// <summary>
/// Request to update session title
/// </summary>
public class UpdateSessionTitleRequest
{
    public string Title { get; set; }
}

/// <summary>
/// Response containing session with messages
/// </summary>
public class ChatSessionWithMessagesDto
{
    public ChatSessionDto Session { get; set; }
    public List<ChatMessageDto> Messages { get; set; }
}

/// <summary>
/// DTO for AI chat session statistics (for display in app editor header)
/// </summary>
public class ChatSessionStatsDto
{
    public Guid SessionId { get; set; }
    public string Title { get; set; }
    public int PromptsSubmitted { get; set; }
    public int TotalTokens { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// DTO for aggregate AI chat statistics for an app
/// </summary>
public class AppChatStatsDto
{
    public int TotalPromptsSubmitted { get; set; }
    public int TotalApiCalls { get; set; }
    public int TotalTokens { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int SessionCount { get; set; }
    public int TotalCacheReadTokens { get; set; }
    public int TotalCacheCreationTokens { get; set; }
}

/// <summary>
/// Per-app activity summary for the admin activity dashboard.
/// Aggregates AI chat session stats across ALL users for a given app.
/// </summary>
public class AppActivityDto
{
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public int TotalPromptsSubmitted { get; set; }
    public int TotalApiCalls { get; set; }
    public int TotalTokens { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int SessionCount { get; set; }
    public DateTime? LastActivityAt { get; set; }
}

/// <summary>
/// A single session with its messages, used inside <see cref="ChatSessionExportDto"/>.
/// </summary>
public class ChatSessionExportItemDto
{
    public ChatSessionDto Session { get; set; }
    public List<ChatMessageDto> Messages { get; set; }
}

/// <summary>
/// Full export payload for all AI chat sessions belonging to a resource (app, flow, etc.).
/// </summary>
public class ChatSessionExportDto
{
    public ChatResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public DateTime ExportedAtUtc { get; set; }
    public int SessionCount { get; set; }
    public List<ChatSessionExportItemDto> Sessions { get; set; }
}
