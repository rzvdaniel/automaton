using Automaton.Mcp.Client.Models;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

/// <summary>
/// Interface for tool result processors
/// </summary>
public interface IToolResultProcessor
{
    string ToolName { get; }
    ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage);
}