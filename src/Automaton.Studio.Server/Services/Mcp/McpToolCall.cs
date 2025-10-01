namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Represents a tool call made by the AI model
/// </summary>
public class McpToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// Represents the result of a tool execution
/// </summary>
public class McpToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents a tool definition for the AI model
/// </summary>
public class McpToolDefinition
{
    public string Type { get; set; } = "function";
    public McpFunctionDefinition Function { get; set; } = new();
}

public class McpFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object Parameters { get; set; } = new object();
}
