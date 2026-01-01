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
