namespace Automaton.Mcp.Client.Models;

/// <summary>
/// Represents a conversation message in the MCP client
/// </summary>
public record ConversationMessage(string Role, string Content)
{
    public string? ToolCallId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Request to start a conversation with the AI
/// </summary>
public class ConversationRequest
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string InitialMessage { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public AiProvider Provider { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<ConversationMessage>? ConversationHistory { get; set; }
    public Dictionary<string, object>? Context { get; set; }
}

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
}

/// <summary>
/// Represents a tool call made by the AI
/// </summary>
public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
}

/// <summary>
/// Result of a tool execution
/// </summary>
public class ToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event fired when conversation is updated
/// </summary>
public class ConversationUpdatedEvent
{
    public string ConversationId { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; set; } = new();
    public List<ToolCall> ToolCalls { get; set; } = new();
    public List<ToolResult> ToolResults { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// AI provider enumeration
/// </summary>
public enum AiProvider
{
    OpenAI = 1,
    Anthropic = 2,
    Google = 3,
    Azure = 4,
    Custom = 99
}

/// <summary>
/// Context for tracking conversations
/// </summary>
public class ConversationContext
{
    public string ConversationId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}