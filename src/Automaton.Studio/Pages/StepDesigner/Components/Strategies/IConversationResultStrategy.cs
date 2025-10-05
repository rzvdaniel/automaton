using Automaton.Mcp.Client.Models;

namespace Automaton.Studio.Pages.StepDesigner.Components.Strategies;

/// <summary>
/// Strategy interface for processing different types of conversation results
/// </summary>
public interface IConversationResultStrategy
{
    Task ProcessResult(ConversationResult result, string userPrompt, bool isCode);
}