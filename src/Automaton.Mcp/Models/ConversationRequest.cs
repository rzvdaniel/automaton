using Automaton.Mcp.Modes;

namespace Automaton.Mcp.Models;

/// <summary>
/// Request to start a conversation with the AI
/// </summary>
public class ConversationRequest
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string InitialMessage { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// Provider name string (e.g., "OpenAI", "Anthropic", "Google")
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
    public Dictionary<string, object>? Context { get; set; }

    /// <summary>
    /// Maximum number of tool call iterations before stopping.
    /// Prevents infinite loops in AI tool execution.
    /// Default: 20
    /// </summary>
    public int MaxIterations { get; set; } = 20;

    /// <summary>
    /// Maximum consecutive failures allowed for the same tool before stopping.
    /// Prevents repeated failing tool calls.
    /// Default: 3
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Interaction mode: <see cref="ConversationModes.Plan"/> (read-only, propose_plan available)
    /// or <see cref="ConversationModes.Agent"/> (write tools, default).
    /// </summary>
    public string Mode { get; set; } = ConversationModes.Agent;
}
