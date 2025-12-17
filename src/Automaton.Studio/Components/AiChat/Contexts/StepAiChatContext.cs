using Automaton.Studio.Models;
using Automaton.Studio.Services.Mcp.SystemPrompts;

namespace Automaton.Studio.Components.AiChat.Contexts;

/// <summary>
/// AI Chat context for Step code generation (Python, PowerShell, JavaScript)
/// </summary>
public class StepAiChatContext : IAiChatContext
{
    private readonly IStepSystemPromptBuilder _promptBuilder;
    private readonly string _currentLanguage;
    private readonly string _currentStepId;
    private readonly List<VariableInfo>? _inputVariables;
    private readonly List<VariableInfo>? _outputVariables;
    private readonly Func<Task<string>>? _getCurrentCode;

    public StepAiChatContext(
        IStepSystemPromptBuilder promptBuilder,
        string currentLanguage,
        string currentStepId,
        List<VariableInfo>? inputVariables = null,
        List<VariableInfo>? outputVariables = null,
        Func<Task<string>>? getCurrentCode = null)
    {
        _promptBuilder = promptBuilder;
        _currentLanguage = currentLanguage;
        _currentStepId = currentStepId;
        _inputVariables = inputVariables;
        _outputVariables = outputVariables;
        _getCurrentCode = getCurrentCode;
    }

    public string InputPlaceholder => "Describe what you want the AI to do... (MCP tools will be used automatically)";

    public string InitialSystemMessage => "AI code panel ready with MCP integration. AI can now see and test your code in real-time.";

    public string Language => _currentLanguage;

    public bool ShowAgentSelector => true;

    public bool ProcessToolResults => true;

    public string BuildSystemPrompt()
    {
        return _promptBuilder.BuildSystemPrompt(_currentLanguage);
    }

    public string GetContextId()
    {
        return _currentStepId;
    }

    public Dictionary<string, object> GetAdditionalContext()
    {
        return new Dictionary<string, object>
        {
            ["language"] = _currentLanguage,
            ["inputVariables"] = _inputVariables ?? new List<VariableInfo>(),
            ["outputVariables"] = _outputVariables ?? new List<VariableInfo>(),
            ["getCurrentCode"] = _getCurrentCode ?? (() => Task.FromResult(string.Empty))
        };
    }
}
