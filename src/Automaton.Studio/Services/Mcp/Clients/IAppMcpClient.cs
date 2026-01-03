using Automaton.Studio.Services.Mcp.ToolProviders;

namespace Automaton.Studio.Services.Mcp.Clients;

/// <summary>
/// Interface for App MCP client that supports both frontend and backend operations
/// </summary>
public interface IAppMcpClient : IMcpClient
{
    /// <summary>
    /// Set the current app context for this MCP client session
    /// </summary>
    void SetCurrentApp(Guid appId);

    /// <summary>
    /// Event fired when backend steps are created, updated, or linked to the app.
    /// UI components can subscribe to refresh their step lists.
    /// </summary>
    event EventHandler<BackendStepsChangedEventArgs>? BackendStepsChanged;
}
