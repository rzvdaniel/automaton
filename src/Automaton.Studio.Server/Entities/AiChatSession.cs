using Automaton.Api.Enums;

namespace Automaton.Api.Entities;

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

    /// <summary>
    /// Number of prompts (user messages) submitted in this session
    /// </summary>
    public int TotalPromptsSubmitted { get; set; } = 0;

    /// <summary>
    /// Total input tokens used in this session
    /// </summary>
    public int TotalInputTokens { get; set; } = 0;

    /// <summary>
    /// Total output tokens generated in this session
    /// </summary>
    public int TotalOutputTokens { get; set; } = 0;

    /// <summary>
    /// Total tokens (input + output) for this session
    /// </summary>
    public int TotalTokens { get; set; } = 0;

    /// <summary>
    /// Last time activity occurred in this session (message sent)
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Total number of LLM API calls made in this session (one per tool-loop iteration).
    /// Unlike TotalPromptsSubmitted (user messages), this counts every round-trip to the provider.
    /// </summary>
    public int TotalApiCalls { get; set; } = 0;

    /// <summary>
    /// The detected complexity tier for this session (Simple, Medium, Large, Enterprise).
    /// Once set it is sticky — never downgraded within the same session.
    /// </summary>
    public string? ComplexityTier { get; set; }

    /// <summary>
    /// Total tokens served from Anthropic prompt cache across this session.
    /// Non-zero only for Anthropic provider sessions with prompt caching enabled.
    /// </summary>
    public int TotalCacheReadTokens { get; set; } = 0;

    /// <summary>
    /// Total tokens written into Anthropic prompt cache across this session.
    /// </summary>
    public int TotalCacheCreationTokens { get; set; } = 0;

    // Navigation properties
    public virtual ICollection<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();
    public virtual ICollection<AiChatSessionUser> AiChatSessionUsers { get; set; } = new List<AiChatSessionUser>();
}
