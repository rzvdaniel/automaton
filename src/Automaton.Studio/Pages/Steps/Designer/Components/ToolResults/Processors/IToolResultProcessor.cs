using Automaton.Mcp.Client.Models;
using Automaton.Studio.Pages.Steps.Designer.Components.ToolResults;

namespace Automaton.Studio.Pages.Steps.Designer.Components.ToolResults.Processors;

/// <summary>
/// Interface for tool result processors
/// </summary>
public interface IToolResultProcessor
{
    string ToolName { get; }
    ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage);
}