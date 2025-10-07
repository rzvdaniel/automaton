using Automaton.Mcp.Client.Models;
using Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Utilities;
using Automaton.Studio.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Pages.StepDesigner.Components.Strategies;

/// <summary>
/// Strategy for processing conversation results in "Ask" mode - generates code for user review
/// </summary>
public class AskModeResultStrategy : IConversationResultStrategy
{
    private readonly List<ChatMessage> _messages;
    private readonly Dictionary<string, string> _pendingCodes;
    private readonly List<CodeVersion> _codeHistory;
    private readonly string _currentLanguage;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;
    private readonly ICodeExtractionService _codeExtractionService;

    public AskModeResultStrategy(
        List<ChatMessage> messages,
        Dictionary<string, string> pendingCodes,
        List<CodeVersion> codeHistory,
        string currentLanguage,
        Microsoft.Extensions.Logging.ILogger logger,
        ICodeExtractionService codeExtractionService)
    {
        _messages = messages;
        _pendingCodes = pendingCodes;
        _codeHistory = codeHistory;
        _currentLanguage = currentLanguage;
        _logger = logger;
        _codeExtractionService = codeExtractionService;
    }

    public async Task ProcessResult(ConversationResult result, string userPrompt, bool isCode)
    {
        if (isCode && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            await ProcessCodeResult(result, userPrompt);
        }
        else
        {
            ProcessTextResult(result);
        }
    }

    private async Task ProcessCodeResult(ConversationResult result, string userPrompt)
    {
        var codeId = Guid.NewGuid().ToString();
        
        // Create a success message without any title - the component will display it cleanly
        var codeMessage = new ChatMessage("AI", string.Empty, DateTime.Now)
        {
            Type = MessageType.CodeSuccess,
            CodeId = codeId,
            GeneratedCode = result.GeneratedCode,
            IsSuccess = true
        };

        _messages.Add(codeMessage);

        // Use the centralized code extraction service
        var codeForEditor = _codeExtractionService.ExtractCodeForEditor(result.GeneratedCode!, _currentLanguage);
        var qualityScore = _codeExtractionService.GetCodeQualityConfidence(codeForEditor, _currentLanguage);
        
        _pendingCodes[codeId] = codeForEditor;

        // Add to code history with truncated description
        var description = userPrompt.Length > 50 ? userPrompt[..47] + "..." : userPrompt;
        _codeHistory.Add(new CodeVersion(codeForEditor, DateTime.Now, description, false));

        _logger.LogInformation("Code generated in Ask mode: {CodeLength} characters, Quality: {Quality:F2}, CodeId: {CodeId}", 
            codeForEditor.Length, qualityScore, codeId);
        
        await Task.CompletedTask;
    }

    private void ProcessTextResult(ConversationResult result)
    {
        var responseMessage = new ChatMessage("AI", result.GeneratedCode ?? "No response received.", DateTime.Now)
        {
            Type = MessageType.Text,
            IsSuccess = true
        };

        _messages.Add(responseMessage);
        _logger.LogInformation("Text response generated in Ask mode: {ContentLength} characters", 
            responseMessage.Content.Length);
    }
}