using Automaton.Studio.Models;

namespace Automaton.Studio.Services.Mcp.EditorBridges;

/// <summary>
/// Shared service for maintaining current web app context across components.
/// Similar to StepContextService but for Apps.
/// </summary>
public interface IAppEditorBridge
{
    /// <summary>
    /// Set the current app being edited
    /// </summary>
    void SetCurrentApp(AppModel app, Func<Task<string>>? getCurrentCodeCallback = null);

    /// <summary>
    /// Get the current web app's ID
    /// </summary>
    string? GetCurrentAppId();

    /// <summary>
    /// Get the current web app's HTML code
    /// </summary>
    Task<string> GetCurrentCodeAsync();

    /// <summary>
    /// Update the web app's HTML code
    /// </summary>
    Task SetCodeAsync(string code);

    /// <summary>
    /// Event fired when web app context changes
    /// </summary>
    event EventHandler<AppEditorBridgeChangedEventArgs>? WebAppContextChanged;

    /// <summary>
    /// Event fired when code is updated (e.g., by MCP tools)
    /// </summary>
    event EventHandler<AppCodeChangedEventArgs>? CodeChanged;
}
