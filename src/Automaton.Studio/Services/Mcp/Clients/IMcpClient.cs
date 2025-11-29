using Automaton.Mcp.Models;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// Base interface for MCP clients used in the Studio.
/// </summary>
public interface IMcpClient
{
    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;
    public Task<ConversationResult> StartConversationAsync(ConversationRequest request, CancellationToken cancellationToken = default);
    public Task<ConversationResult> ContinueConversationAsync(string conversationId, string message, CancellationToken cancellationToken = default);
    public Task<bool> EndConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
