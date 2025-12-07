using Automaton.Studio.Models;
using Automaton.Studio.Services.Mcp.SystemPrompts;

namespace Automaton.Studio.Components.AiChat.Contexts;

/// <summary>
/// AI Chat context for unified App + Backend Step generation
/// </summary>
public class UnifiedAppAiChatContext : IAiChatContext
{
    private readonly IUnifiedAppSystemPromptBuilder _promptBuilder;
    private readonly string _currentWebAppId;
    private readonly Func<Task<string>>? _getCurrentCode;
    private readonly Func<Task<List<LinkedStepInfo>>>? _getLinkedSteps;

    public UnifiedAppAiChatContext(
        IUnifiedAppSystemPromptBuilder promptBuilder,
        string currentWebAppId,
        Func<Task<string>>? getCurrentCode = null,
        Func<Task<List<LinkedStepInfo>>>? getLinkedSteps = null)
    {
        _promptBuilder = promptBuilder;
        _currentWebAppId = currentWebAppId;
        _getCurrentCode = getCurrentCode;
        _getLinkedSteps = getLinkedSteps;
    }

    public string Title => "AI Full-Stack Generation";

    public string InputPlaceholder => "Describe your complete app (frontend + backend)...";

    public string InitialSystemMessage => "AI full-stack generation ready. I can create both frontend (HTML/CSS/JavaScript) and backend (Custom Steps) in a single interaction.";

    public string Language => "html";

    public bool ShowAgentSelector => true;  // Enable Agent/Ask selector

    public bool ProcessToolResults => true; // Enable MCP tools

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
        var context = new Dictionary<string, object>
        {
            ["language"] = "html",
            ["getCurrentCode"] = _getCurrentCode ?? (() => Task.FromResult(string.Empty))
        };

        if (_getLinkedSteps != null)
        {
            context["getLinkedSteps"] = _getLinkedSteps;
        }

        return context;
    }
}
