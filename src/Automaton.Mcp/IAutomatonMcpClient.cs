using Automaton.Mcp.Models;
using Automaton.Mcp.Services;

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
    /// Set the tool provider for executing MCP tools (allows dynamic tool provider selection)
    /// </summary>
    void SetToolProvider(IMcpToolProvider? toolProvider);
    
    /// <summary>
    /// Event fired when conversation is updated with new messages or tool results
    /// </summary>
    event EventHandler<ConversationUpdatedEvent> ConversationUpdated;
}