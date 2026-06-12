namespace Automaton.Ai.Models;

/// <summary>
/// Result of a conversation with the AI
/// </summary>
public class ConversationResult
{
    public string ConversationId { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public List<ToolCall> ToolCalls { get; set; } = new();
    public List<ToolResult> ToolResults { get; set; } = new();
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GeneratedCode { get; set; }

    /// <summary>
    /// Total number of LLM API round-trips made during this turn (one per provider loop iteration).
    /// Populated from <see cref="ProviderResult.ApiCallCount"/> returned by the provider.
    /// </summary>
    public int ApiCallCount { get; set; }
}
