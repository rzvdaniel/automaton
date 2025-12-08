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

    // Tools that save code and don't need additional code extraction from final response
    private static readonly HashSet<string> CodeSavingTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "save_code",        // Step code saving
        "save_app_code",    // App code saving
        "save_step_code"    // Backend step code saving
    };

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
        // Check if code-saving tools were already executed
        // If so, the code is already in the editor via MCP tool execution
        var codeSavingToolExecuted = WasCodeSavingToolExecuted();
        
        if (codeSavingToolExecuted)
        {
            _logger.LogInformation("Code-saving tool was executed. Skipping code extraction from final response to avoid overwriting.");
            // The code is already saved via MCP tools, don't extract from final response
            ProcessTextResult(result);
            return;
        }

        if (isCode && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            await ProcessCodeResult(result);
        }
        else
        {
            ProcessTextResult(result);
        }
    }

    /// <summary>
    /// Checks if any code-saving tool was executed in this conversation
    /// </summary>
    private bool WasCodeSavingToolExecuted()
    {
        return _messages.Any(m => 
            m.Type == MessageType.ToolExecution && 
            !string.IsNullOrEmpty(m.ToolName) &&
            CodeSavingTools.Contains(m.ToolName) &&
            m.ToolStatus == "success");
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