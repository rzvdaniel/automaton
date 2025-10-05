using Automaton.Studio.Pages.StepDesigner.Components.Strategies;
using Automaton.Studio.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.StepDesigner.Components.Factories;

/// <summary>
/// Factory for creating conversation result strategy instances using Factory Method pattern
/// </summary>
public class ConversationResultStrategyFactory
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly ICodeExtractionService _codeExtractionService;

    public ConversationResultStrategyFactory(
        Microsoft.Extensions.Logging.ILogger logger,
        ICodeExtractionService codeExtractionService)
    {
        _logger = logger;
        _codeExtractionService = codeExtractionService;
    }

    /// <summary>
    /// Creates the appropriate strategy based on the selected agent mode
    /// </summary>
    /// <param name="selectedAgent">The agent mode ("Ask" or "Agent")</param>
    /// <param name="messages">Chat messages list</param>
    /// <param name="pendingCodes">Pending codes dictionary (used only in Ask mode)</param>
    /// <param name="codeHistory">Code history list (used only in Ask mode)</param>
    /// <param name="onCodeGenerated">Code generation callback (used only in Agent mode)</param>
    /// <param name="currentLanguage">Current programming language</param>
    /// <returns>Appropriate strategy instance</returns>
    public IConversationResultStrategy CreateStrategy(
        string selectedAgent,
        List<ChatMessage> messages,
        Dictionary<string, string> pendingCodes,
        List<CodeVersion> codeHistory,
        EventCallback<string> onCodeGenerated,
        string currentLanguage)
    {
        return selectedAgent switch
        {
            "Ask" => new AskModeResultStrategy(
                messages,
                pendingCodes,
                codeHistory,
                currentLanguage,
                _logger,
                _codeExtractionService),
                
            "Agent" => new AgentModeResultStrategy(
                messages,
                onCodeGenerated,
                currentLanguage,
                _logger,
                _codeExtractionService),
                
            _ => throw new ArgumentException($"Unknown agent mode: {selectedAgent}", nameof(selectedAgent))
        };
    }

    /// <summary>
    /// Gets the supported agent modes
    /// </summary>
    public static IReadOnlyList<string> SupportedAgentModes => new[] { "Ask", "Agent" };
}