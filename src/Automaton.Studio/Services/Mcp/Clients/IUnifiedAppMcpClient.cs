namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// Interface for unified App MCP client that supports both frontend and backend operations
/// </summary>
public interface IUnifiedAppMcpClient : IMcpClient
{
    /// <summary>
    /// Set the current app context for this MCP client session
    /// </summary>
    void SetCurrentApp(Guid appId);
}
