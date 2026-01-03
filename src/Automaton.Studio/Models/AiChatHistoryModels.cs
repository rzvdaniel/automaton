namespace Automaton.Studio.Models;

/// <summary>
/// Type of resource associated with a chat session
/// </summary>
public enum ChatResourceType
{
    CustomStep = 0,
    WebApp = 1
}

/// <summary>
/// Type of AI chat message
/// </summary>
public enum ChatMessageType
{
    Text = 0,
    Generating = 1,
    CodeSuccess = 2,
    CodeError = 3,
    System = 4,
    ToolExecution = 5,
    Reasoning = 6,
    OperationInProgress = 7,
    OperationComplete = 8
}

/// <summary>
/// DTO for AI chat session information
/// </summary>
public class ChatSessionModel
{
    public Guid Id { get; set; }
    public ChatResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// DTO for AI chat message information
/// </summary>
public class ChatMessageModel
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatMessageType MessageType { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request to create a new chat session
/// </summary>
public class CreateChatSessionRequest
{
    public ChatResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string Title { get; set; } = "New Chat";
    public string? Provider { get; set; }
    public string? Model { get; set; }
}

/// <summary>
/// Request to save a chat message
/// </summary>
public class SaveChatMessageRequest
{
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatMessageType MessageType { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// Request to save multiple chat messages (batch operation)
/// </summary>
public class SaveChatMessagesRequest
{
    public Guid SessionId { get; set; }
    public List<ChatMessageItem> Messages { get; set; } = new();
}

/// <summary>
/// Individual message item for batch save
/// </summary>
public class ChatMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatMessageType MessageType { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// Request to update session title
/// </summary>
public class UpdateSessionTitleRequest
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Response containing session with messages
/// </summary>
public class ChatSessionWithMessagesModel
{
    public ChatSessionModel Session { get; set; } = new();
    public List<ChatMessageModel> Messages { get; set; } = new();
}
