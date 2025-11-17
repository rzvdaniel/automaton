using Automaton.Mcp.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Service interface for extracting code from various AI conversation contexts
/// </summary>
public interface ICodeExtractionService
{
    /// <summary>
    /// Extract clean code suitable for editor application from AI response
    /// </summary>
    /// <param name="response">AI response that may contain code blocks, explanations, etc.</param>
    /// <param name="language">Programming language to extract (Python, C#, etc.)</param>
    /// <returns>Clean code ready for editor, or empty string if no code found</returns>
    string ExtractCodeForEditor(string response, string language);

    /// <summary>
    /// Extract the final working code from an MCP conversation result and messages
    /// Priority: get_current_code results > save_code success > direct AI response
    /// </summary>
    /// <param name="result">MCP conversation result</param>
    /// <param name="messages">Chat messages from the conversation</param>
    /// <param name="language">Programming language context</param>
    /// <returns>Final working code from the conversation</returns>
    string? ExtractFinalCodeFromConversation(ConversationResult result, IEnumerable<object> messages, string language);

    /// <summary>
    /// Extract code from a list of conversation messages (simple fallback)
    /// </summary>
    /// <param name="messages">List of conversation messages</param>
    /// <returns>Content from the last assistant message, or empty string</returns>
    string ExtractCodeFromMessages(IEnumerable<ConversationMessage> messages);

    /// <summary>
    /// Determine the confidence level of extracted code quality
    /// </summary>
    /// <param name="code">Extracted code to analyze</param>
    /// <param name="language">Programming language context</param>
    /// <returns>Confidence score from 0.0 (low) to 1.0 (high)</returns>
    double GetCodeQualityConfidence(string code, string language);
}