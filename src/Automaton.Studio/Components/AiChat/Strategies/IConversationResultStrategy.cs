using Automaton.Mcp.Models;

namespace Automaton.Studio.Components.AiChat.Strategies;

/// <summary>
/// Strategy interface for processing different types of conversation results
/// </summary>
public interface IConversationResultStrategy
{
    Task ProcessResult(ConversationResult result, string userPrompt, bool isCode);
}