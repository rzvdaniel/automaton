using Automaton.Mcp.Client.Models;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Utilities;
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

    public AgentModeResultStrategy(
        List<ChatMessage> messages,
        EventCallback<string> onCodeGenerated,
        string currentLanguage,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        _messages = messages;
        _onCodeGenerated = onCodeGenerated;
        _currentLanguage = currentLanguage;
        _logger = logger;
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
        var codeMessage = new ChatMessage("AI", "Code generated and applied with MCP tools:", DateTime.Now)
        {
            Type = MessageType.CodeSuccess,
            GeneratedCode = result.GeneratedCode,
            IsSuccess = true
        };

        _messages.Add(codeMessage);

        // Auto-apply the generated code to the Monaco editor
        var codeForEditor = CodeExtractionUtility.ExtractCodeForEditor(result.GeneratedCode!, _currentLanguage);
        if (_onCodeGenerated.HasDelegate && !string.IsNullOrEmpty(codeForEditor))
        {
            await _onCodeGenerated.InvokeAsync(codeForEditor);
            _logger.LogInformation("Auto-applied generated code to Monaco editor in Agent mode: {CodeLength} characters", 
                codeForEditor.Length);
        }
        else
        {
            _logger.LogWarning("Failed to auto-apply code in Agent mode: OnCodeGenerated callback not available or code is empty");
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