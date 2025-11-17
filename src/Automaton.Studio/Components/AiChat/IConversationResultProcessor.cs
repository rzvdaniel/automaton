using Automaton.Mcp.Models;

namespace Automaton.Studio.Components.AiChat;

/// <summary>
/// Interface for processing conversation results using Strategy Pattern
/// </summary>
public interface IConversationResultProcessor
{
    Task ProcessSuccessResult(ConversationResult result, string userPrompt, string selectedAgent);
}
