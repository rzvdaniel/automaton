using Automaton.Mcp.Client.Models;

namespace Automaton.Mcp.Client;

/// <summary>
/// Main interface for the Automaton MCP client
/// </summary>
public interface IAutomatonMcpClient
{
    /// <summary>
    /// Start a new conversation with the AI
    /// </summary>
    Task<ConversationResult> StartConversationAsync(ConversationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Continue an existing conversation
    /// </summary>
    Task<ConversationResult> ContinueConversationAsync(string conversationId, string message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// End a conversation
    /// </summary>
    Task<bool> EndConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event fired when conversation is updated with new messages or tool results
    /// </summary>
    event EventHandler<ConversationUpdatedEvent> ConversationUpdated;
}