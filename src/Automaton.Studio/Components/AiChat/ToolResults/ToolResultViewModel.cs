using Automaton.Studio.Components.AiChat.ToolResults.Processors;

namespace Automaton.Studio.Components.AiChat.ToolResults;

/// <summary>
/// Represents the result of processing a tool execution for display
/// </summary>
public class ToolResultViewModel
{
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StatusIcon { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? Summary { get; set; }
    public string? CodeContent { get; set; }
    public string? Language { get; set; }
    public int? LineCount { get; set; }
    public string? ErrorMessage { get; set; }
    public ToolResultType ResultType { get; set; }
}
