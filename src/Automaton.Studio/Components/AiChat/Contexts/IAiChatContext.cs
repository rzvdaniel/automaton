namespace Automaton.Studio.Components.AiChat.Contexts;

/// <summary>
/// Type of MCP tool provider to use for AI chat
/// </summary>
public enum McpToolProviderType
{
    /// <summary>
    /// Step-specific tools (Python, PowerShell, JavaScript code editing and execution)
    /// </summary>
    Step,
    
    /// <summary>
    /// WebApp-specific tools (HTML/CSS/JavaScript editing)
    /// </summary>
    WebApp
}

/// <summary>
/// Defines the context for AI chat interactions, allowing different scenarios (Steps, WebApps, Flows) to customize behavior
/// </summary>
public interface IAiChatContext
{
    /// <summary>
    /// Title displayed in the chat panel header
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Placeholder text for the input area
    /// </summary>
    string InputPlaceholder { get; }

    /// <summary>
    /// Initial system message shown when chat loads
    /// </summary>
    string InitialSystemMessage { get; }

    /// <summary>
    /// Language for code generation (e.g., "Python", "html")
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Whether to show the Agent/Ask selector
    /// </summary>
    bool ShowAgentSelector { get; }

    /// <summary>
    /// Whether tool results should be processed (Step-specific MCP tools like execute_code)
    /// </summary>
    bool ProcessToolResults { get; }

    /// <summary>
    /// The type of MCP tool provider to use for this context
    /// </summary>
    McpToolProviderType ToolProviderType { get; }

    /// <summary>
    /// Build the system prompt for AI conversations
    /// </summary>
    string BuildSystemPrompt();

    /// <summary>
    /// Get the context ID for this conversation (e.g., StepId, AppId)
    /// </summary>
    string GetContextId();

    /// <summary>
    /// Get additional context data to pass to MCP conversation
    /// </summary>
    Dictionary<string, object> GetAdditionalContext();
}
