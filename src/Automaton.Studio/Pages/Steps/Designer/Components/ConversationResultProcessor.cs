using Automaton.Mcp.Client.Models;
using Automaton.Studio.Pages.Steps.Designer.Components.Factories;
using Automaton.Studio.Pages.Steps.Designer.Components.Services;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Pages.Steps.Designer.Components;

/// <summary>
/// Interface for processing conversation results using Strategy Pattern
/// </summary>
public interface IConversationResultProcessor
{
    Task ProcessSuccessResult(ConversationResult result, string userPrompt, string selectedAgent);
}

/// <summary>
/// Orchestrator for conversation result processing using Strategy Pattern
/// This class now delegates to specific strategies based on the agent mode
/// </summary>
public class ConversationResultProcessor : IConversationResultProcessor
{
    private readonly ConversationResultStrategyFactory _strategyFactory;
    private readonly List<ChatMessage> _messages;
    private readonly Dictionary<string, string> _pendingCodes;
    private readonly List<CodeVersion> _codeHistory;
    private readonly EventCallback<string> _onCodeGenerated;
    private readonly string _currentLanguage;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

    public ConversationResultProcessor(
        List<ChatMessage> messages,
        Dictionary<string, string> pendingCodes,
        List<CodeVersion> codeHistory,
        EventCallback<string> onCodeGenerated,
        string currentLanguage,
        Microsoft.Extensions.Logging.ILogger logger,
        ICodeExtractionService codeExtractionService)
    {
        _messages = messages;
        _pendingCodes = pendingCodes;
        _codeHistory = codeHistory;
        _onCodeGenerated = onCodeGenerated;
        _currentLanguage = currentLanguage;
        _logger = logger;
        _strategyFactory = new ConversationResultStrategyFactory(logger, codeExtractionService);
    }

    public async Task ProcessSuccessResult(ConversationResult result, string userPrompt, string selectedAgent)
    {
        try
        {
            // Use CodeDetectionService to determine if result contains code
            bool isCode = CodeDetectionService.IsActualCode(result.GeneratedCode);
            
            _logger.LogInformation("Processing conversation result in {AgentMode} mode. IsCode: {IsCode}, CodeLength: {CodeLength}", 
                selectedAgent, isCode, result.GeneratedCode?.Length ?? 0);

            // Create appropriate strategy and process result
            var strategy = _strategyFactory.CreateStrategy(
                selectedAgent,
                _messages,
                _pendingCodes,
                _codeHistory,
                _onCodeGenerated,
                _currentLanguage);

            await strategy.ProcessResult(result, userPrompt, isCode);

            _logger.LogInformation("Successfully processed conversation result using {StrategyType}", 
                strategy.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing conversation result in {AgentMode} mode", selectedAgent);
            
            // Add error message to chat
            var errorMessage = new ChatMessage("AI", $"Error processing result: {ex.Message}", DateTime.Now)
            {
                Type = MessageType.CodeError,
                IsSuccess = false
            };
            _messages.Add(errorMessage);
            
            throw; // Re-throw to allow higher-level error handling
        }
    }
}