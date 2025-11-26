using Automaton.Mcp.Models;
using Automaton.Studio.Components.AiChat.ToolResults.Processors;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Components.AiChat.Strategies;

/// <summary>
/// Strategy for processing conversation results in "Agent" mode - auto-applies code to editor
/// </summary>
public class AgentModeResultStrategy : IConversationResultStrategy
{
    private readonly List<ChatMessage> _messages;
    private readonly EventCallback<string> _onCodeGenerated;
    private readonly string _currentLanguage;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly ICodeExtractionService _codeExtractionService;

    public AgentModeResultStrategy(
        List<ChatMessage> messages,
        EventCallback<string> onCodeGenerated,
        string currentLanguage,
        Microsoft.Extensions.Logging.ILogger logger,
        ICodeExtractionService codeExtractionService)
    {
        _messages = messages;
        _onCodeGenerated = onCodeGenerated;
        _currentLanguage = currentLanguage;
        _logger = logger;
        _codeExtractionService = codeExtractionService;
    }

    public async Task ProcessResult(ConversationResult result, string userPrompt, bool isCode)
    {
        if (isCode && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            await ProcessCodeResult(result);
        }
        else
        {
            ProcessTextResult(result);
        }
    }

    private async Task ProcessCodeResult(ConversationResult result)
    {
        // In Agent mode, just auto-apply the code without adding any extra messages
        // The reasoning messages from OnConversationUpdated are already displayed
        var codeForEditor = _codeExtractionService.ExtractCodeForEditor(result.GeneratedCode!, _currentLanguage);
        var qualityScore = _codeExtractionService.GetCodeQualityConfidence(codeForEditor, _currentLanguage);
        
        if (_onCodeGenerated.HasDelegate && !string.IsNullOrEmpty(codeForEditor))
        {
            await _onCodeGenerated.InvokeAsync(codeForEditor);
            _logger.LogInformation("Auto-applied generated code to Monaco editor in Agent mode: {CodeLength} characters, Quality: {Quality:F2}", 
                codeForEditor.Length, qualityScore);
        }
        else
        {
            _logger.LogWarning("Failed to auto-apply code in Agent mode: OnCodeGenerated callback not available or code is empty. Quality: {Quality:F2}", 
                qualityScore);
        }
    }

    private void ProcessTextResult(ConversationResult result)
    {
        // Text messages are already added via OnConversationUpdated handler in AiChatPanel
        // which handles real-time streaming of AI responses. We don't add them here to avoid duplicates.
        // This method is kept for potential future use cases where text-only responses need special handling.
        _logger.LogInformation("Text response received in Agent mode (already displayed via real-time updates): {ContentLength} characters", 
            result.GeneratedCode?.Length ?? 0);
    }
}