using Automaton.Studio.Services.Mcp;

namespace Automaton.Studio.Components.AiChat.Contexts;

/// <summary>
/// AI Chat context for WebApp HTML/CSS/JavaScript generation
/// </summary>
public class WebAppAiChatContext : IAiChatContext
{
    private readonly IWebAppSystemPromptBuilder _promptBuilder;
    private readonly string _currentWebAppId;
    private readonly Func<Task<string>>? _getCurrentCode;

    public WebAppAiChatContext(
        IWebAppSystemPromptBuilder promptBuilder,
        string currentWebAppId,
        Func<Task<string>>? getCurrentCode = null)
    {
        _promptBuilder = promptBuilder;
        _currentWebAppId = currentWebAppId;
        _getCurrentCode = getCurrentCode;
    }

    public string Title => "AI Web Code Generation";

    public string InputPlaceholder => "Describe the web page you want to create (HTML/CSS/JavaScript)...";

    public string InitialSystemMessage => "AI web code generation ready with MCP integration. AI can now see and edit your HTML code in real-time.";

    public string Language => "html";

    public bool ShowAgentSelector => true;  // Enable Agent/Ask selector for WebApps

    public bool ProcessToolResults => true; // Enable MCP tools for WebApps

    public McpToolProviderType ToolProviderType => McpToolProviderType.WebApp;

    public string BuildSystemPrompt()
    {
        return _promptBuilder.BuildSystemPrompt();
    }

    public string GetContextId()
    {
        return _currentWebAppId;
    }

    public Dictionary<string, object> GetAdditionalContext()
    {
        return new Dictionary<string, object>
        {
            ["language"] = "html",
            ["getCurrentCode"] = _getCurrentCode ?? (() => Task.FromResult(string.Empty)),
            ["toolProviderType"] = McpToolProviderType.WebApp.ToString()
        };
    }
}
