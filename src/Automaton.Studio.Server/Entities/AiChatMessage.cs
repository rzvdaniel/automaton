using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Represents a single message in an AI chat session
/// </summary>
public class AiChatMessage
{
    public Guid Id { get; set; }

    /// <summary>
    /// The session this message belongs to
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Message role (User, AI, System)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The main content of the message
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Type of message (Text, CodeSuccess, ToolExecution, etc.)
    /// </summary>
    public ChatMessageType MessageType { get; set; }

    /// <summary>
    /// Additional metadata stored as JSON (CodeId, GeneratedCode, ToolName, ToolResult, etc.)
    /// </summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual AiChatSession Session { get; set; } = null!;
}
