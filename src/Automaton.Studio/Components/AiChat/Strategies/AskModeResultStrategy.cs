using Automaton.Mcp.Models;
using Automaton.Studio.Components.AiChat.Models;
using Automaton.Studio.Components.AiChat.ToolResults.Processors;
using Automaton.Studio.Services;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Components.AiChat.Strategies;

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
        // Text messages are already added via OnConversationUpdated handler in AiChatPanel
        // which handles real-time streaming of AI responses. We don't add them here to avoid duplicates.
        // This method is kept for potential future use cases where text-only responses need special handling.
        _logger.LogInformation("Text response received in Ask mode (already displayed via real-time updates): {ContentLength} characters", 
            result.GeneratedCode?.Length ?? 0);
    }
}