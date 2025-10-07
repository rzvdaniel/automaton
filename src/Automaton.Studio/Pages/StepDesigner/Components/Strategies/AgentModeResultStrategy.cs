using Automaton.Mcp.Client.Models;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Utilities;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Pages.StepDesigner.Components.Strategies;

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
        // Create a success message without any title - the component will display it cleanly
        var codeMessage = new ChatMessage("AI", string.Empty, DateTime.Now)
        {
            Type = MessageType.CodeSuccess,
            GeneratedCode = result.GeneratedCode,
            IsSuccess = true
        };

        _messages.Add(codeMessage);

        // Use the centralized code extraction service
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
        var responseMessage = new ChatMessage("AI", result.GeneratedCode ?? "No response received.", DateTime.Now)
        {
            Type = MessageType.Text,
            IsSuccess = true
        };

        _messages.Add(responseMessage);
        _logger.LogInformation("Text response generated in Agent mode: {ContentLength} characters", 
            responseMessage.Content.Length);
    }
}