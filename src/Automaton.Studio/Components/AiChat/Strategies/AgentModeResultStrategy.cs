using Automaton.Mcp.Models;
using Automaton.Studio.Components.AiChat.ToolResults.Processors;
using Automaton.Studio.Services;
using Automaton.Studio.Services.Mcp;
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

        // Only process as code if:
        // 1. isCode is true (CodeDetectionService detected code patterns)
        // 2. AND the response actually contains code blocks (```
        // This prevents explanatory text from being treated as code
        var hasCodeBlocks = !string.IsNullOrEmpty(result.GeneratedCode) && 
                            result.GeneratedCode.Contains("```");

        if (isCode && hasCodeBlocks && !string.IsNullOrEmpty(result.GeneratedCode))
        {
            await ProcessCodeResult(result);
        }
        else
        {
            if (isCode && !hasCodeBlocks)
            {
                _logger.LogInformation("Response was detected as code but has no code blocks - treating as text to prevent applying explanatory text to editor");
            }
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
            CodeSavingTools.IsCodeSavingTool(m.ToolName) &&
            m.ToolStatus == "success");
    }

    private async Task ProcessCodeResult(ConversationResult result)
    {
        // In Agent mode, just auto-apply the code without adding any extra messages
        // The reasoning messages from OnConversationUpdated are already displayed
        var codeForEditor = _codeExtractionService.ExtractCodeForEditor(result.GeneratedCode!, _currentLanguage);
        
        // Double-check that extracted code is not empty and doesn't look like explanatory text
        if (string.IsNullOrWhiteSpace(codeForEditor))
        {
            _logger.LogWarning("Code extraction returned empty result. Skipping auto-apply.");
            return;
        }
        
        // Additional safety check: if the "code" looks like a summary/explanation, don't apply it
        if (LooksLikeExplanatoryText(codeForEditor))
        {
            _logger.LogWarning("Extracted content looks like explanatory text, not code. Skipping auto-apply. Content preview: {Preview}", 
                codeForEditor.Length > 100 ? codeForEditor[..100] + "..." : codeForEditor);
            return;
        }
        
        var qualityScore = _codeExtractionService.GetCodeQualityConfidence(codeForEditor, _currentLanguage);
        
        if (_onCodeGenerated.HasDelegate)
        {
            await _onCodeGenerated.InvokeAsync(codeForEditor);
            _logger.LogInformation("Auto-applied generated code to Monaco editor in Agent mode: {CodeLength} characters, Quality: {Quality:F2}", 
                codeForEditor.Length, qualityScore);
        }
        else
        {
            _logger.LogWarning("Failed to auto-apply code in Agent mode: OnCodeGenerated callback not available. Quality: {Quality:F2}", 
                qualityScore);
        }
    }

    /// <summary>
    /// Checks if the content looks like explanatory/summary text rather than actual code
    /// </summary>
    private bool LooksLikeExplanatoryText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return true;
            
        var lowerContent = content.ToLowerInvariant();
        
        // Common phrases in AI explanatory responses that are NOT code
        var explanatoryPhrases = new[]
        {
            "summary of changes",
            "what was done",
            "variables created",
            "variables modified",
            "code highlights",
            "here's what",
            "here is what",
            "the code has been",
            "i've updated",
            "i have updated",
            "successfully executed",
            "execution complete",
            "the following changes",
            "changes made:",
            "modifications:",
            "output:",
            "result:",
        };
        
        // If content starts with or contains these phrases prominently, it's likely explanatory text
        foreach (var phrase in explanatoryPhrases)
        {
            if (lowerContent.Contains(phrase))
            {
                return true;
            }
        }
        
        // If content is predominantly prose (sentences with periods, question marks, etc.)
        // and doesn't have clear code structure, treat as explanatory
        var sentenceEndCount = content.Count(c => c == '.' || c == '?' || c == '!');
        var codeSymbolCount = content.Count(c => c == '{' || c == '}' || c == '(' || c == ')' || c == ';' || c == ':');
        
        // If there are more sentence endings than code symbols, likely explanatory text
        if (sentenceEndCount > 3 && sentenceEndCount > codeSymbolCount)
        {
            // But only if it doesn't look like actual code (no function definitions, class definitions, etc.)
            if (!content.Contains("def ") && !content.Contains("class ") && 
                !content.Contains("function ") && !content.Contains("public ") &&
                !content.Contains("private ") && !content.Contains("import "))
            {
                return true;
            }
        }
        
        return false;
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
