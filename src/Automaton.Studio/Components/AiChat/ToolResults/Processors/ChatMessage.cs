namespace Automaton.Studio.Components.AiChat.ToolResults.Processors;

public record ChatMessage
{
    public ChatMessage(string role, string content, DateTime time)
    {
        Role = role;
        Content = content;
        Time = time;
    }

    public string Role { get; init; }
    public string Content { get; init; }
    public DateTime Time { get; init; }
    public bool IsGenerating { get; init; } = false;
    public string? CodeId { get; init; }
    public bool IsSuccess { get; init; } = true;
    public string? GeneratedCode { get; init; }
    public MessageType Type { get; init; } = MessageType.Text;
    public string? ToolName { get; init; }
    public string? ToolResult { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolStatus { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolError { get; init; }
    public ToolResultViewModel? ToolResultViewModel { get; init; }
}
