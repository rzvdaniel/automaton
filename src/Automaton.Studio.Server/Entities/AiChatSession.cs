using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Represents a chat session with an AI model for a specific resource (Step or WebApp)
/// </summary>
public class AiChatSession
{
    public Guid Id { get; set; }

    /// <summary>
    /// The type of resource this chat session is associated with
    /// </summary>
    public ChatResourceType ResourceType { get; set; }

    /// <summary>
    /// The ID of the resource (CustomStep or WebApp)
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// User-friendly title for the session
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// AI provider used in this session (OpenAI, Anthropic, Google, Azure)
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// AI model used in this session (gpt-4, claude-3, etc.)
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Indicates if this is the currently active session for the resource
    /// </summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();
    public virtual ICollection<AiChatSessionUser> AiChatSessionUsers { get; set; } = new List<AiChatSessionUser>();
}
