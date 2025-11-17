using Automaton.Mcp.Models;
using Automaton.Studio.Pages.Steps.Designer.Components.ToolResults.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Services;

/// <summary>
/// Centralized service for extracting code from various AI conversation contexts
/// Consolidates all code extraction logic into a single, testable service
/// </summary>
public class CodeExtractionService : ICodeExtractionService
{
    private readonly ILogger<CodeExtractionService> _logger;

    public CodeExtractionService(ILogger<CodeExtractionService> logger)
    {
        _logger = logger;
    }

    public string ExtractCodeForEditor(string response, string language)
    {
        try
        {
            // Delegate to the utility for now, but we own the interface
            return CodeExtractionUtility.ExtractCodeForEditor(response, language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code for editor from response. Language: {Language}, ResponseLength: {Length}", 
                language, response?.Length ?? 0);
            return string.Empty;
        }
    }

    public string? ExtractFinalCodeFromConversation(ConversationResult result, IEnumerable<object> messages, string language)
    {
        try
        {
            _logger.LogDebug("Extracting final code from conversation. Language: {Language}", language);

            // Strategy 1: Prioritize tool results that contain working code
            if (result.ToolResults?.Any() == true)
            {
                var codeFromTools = ExtractCodeFromToolResults(result.ToolResults, language);
                if (!string.IsNullOrEmpty(codeFromTools))
                {
                    _logger.LogDebug("Extracted final code from tool results: {Length} characters", codeFromTools.Length);
                    return codeFromTools;
                }
            }

            // Strategy 2: Use the GeneratedCode from the conversation result
            if (!string.IsNullOrEmpty(result.GeneratedCode))
            {
                var extractedCode = ExtractCodeForEditor(result.GeneratedCode, language);
                if (!string.IsNullOrEmpty(extractedCode))
                {
                    _logger.LogDebug("Extracted final code from ConversationResult.GeneratedCode: {Length} characters", extractedCode.Length);
                    return extractedCode;
                }
            }

            // Strategy 3: Analyze conversation messages for code content
            if (result.Messages?.Any() == true)
            {
                var codeFromMessages = ExtractCodeFromMessages(result.Messages);
                if (!string.IsNullOrEmpty(codeFromMessages))
                {
                    var extractedCode = ExtractCodeForEditor(codeFromMessages, language);
                    if (!string.IsNullOrEmpty(extractedCode))
                    {
                        _logger.LogDebug("Extracted final code from conversation messages: {Length} characters", extractedCode.Length);
                        return extractedCode;
                    }
                }
            }

            _logger.LogWarning("No code found in conversation result, tool results, or messages");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting final code from conversation. Language: {Language}", language);
            return null;
        }
    }

    /// <summary>
    /// Extract code from MCP tool results, prioritizing certain tools
    /// </summary>
    private string? ExtractCodeFromToolResults(IEnumerable<ToolResult> toolResults, string language)
    {
        try
        {
            var toolList = toolResults.ToList();
            
            // Since ToolResult doesn't have ToolName, we'll work with what we have
            // Priority 1: Any successful tool result with code content
            var successfulResults = toolList
                .Where(tr => tr.IsSuccess && !string.IsNullOrEmpty(tr.Result))
                .Where(tr => ContainsCodeContent(tr.Result!))
                .ToList();

            if (successfulResults.Any())
            {
                var bestResult = successfulResults.Last(); // Most recent
                var extracted = ExtractCodeForEditor(bestResult.Result!, language);
                if (!string.IsNullOrEmpty(extracted))
                {
                    _logger.LogDebug("Found code from successful tool result: {Length} characters", extracted.Length);
                    return extracted;
                }
            }

            // Priority 2: Any successful tool result (even without obvious code patterns)
            var anySuccessfulResults = toolList
                .Where(tr => tr.IsSuccess && !string.IsNullOrEmpty(tr.Result))
                .ToList();

            if (anySuccessfulResults.Any())
            {
                var bestResult = anySuccessfulResults.Last();
                var extracted = ExtractCodeForEditor(bestResult.Result!, language);
                if (!string.IsNullOrEmpty(extracted))
                {
                    _logger.LogDebug("Found code from any successful tool result: {Length} characters", extracted.Length);
                    return extracted;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code from tool results");
            return null;
        }
    }

    public string ExtractCodeFromMessages(IEnumerable<ConversationMessage> messages)
    {
        try
        {
            var messageList = messages.ToList();
            _logger.LogDebug("Analyzing {MessageCount} messages for code extraction", messageList.Count);

            // Strategy 1: Look for the most recent message with actual code content
            var messagesWithCode = messageList
                .Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Where(m => ContainsCodeContent(m.Content))
                .ToList();

            if (messagesWithCode.Any())
            {
                var bestCodeMessage = messagesWithCode.Last(); // Most recent code
                _logger.LogDebug("Found code in message with {Length} characters", bestCodeMessage.Content.Length);
                return bestCodeMessage.Content;
            }

            // Strategy 2: Look for tool results that might contain code
            var toolMessages = messageList
                .Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                .Where(m => ContainsToolResults(m.Content))
                .ToList();

            if (toolMessages.Any())
            {
                var toolMessage = toolMessages.Last();
                _logger.LogDebug("Found tool results in message with {Length} characters", toolMessage.Content.Length);
                return toolMessage.Content;
            }

            // Strategy 3: Fallback to last assistant message (original behavior)
            var lastAssistantMessage = messageList.LastOrDefault(m => 
                string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
            
            var content = lastAssistantMessage?.Content ?? string.Empty;
            
            _logger.LogDebug("Fallback: extracted from last assistant message. Content length: {Length}", content.Length);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting code from messages. MessageCount: {Count}", 
                messages?.Count() ?? 0);
            return string.Empty;
        }
    }

    /// <summary>
    /// Determines if a message contains actual code content (not just explanatory text)
    /// </summary>
    private bool ContainsCodeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Look for code block markers
        if (content.Contains("```"))
            return true;

        // Look for programming language keywords
        var codeIndicators = new[]
        {
            "def ", "class ", "import ", "from ",     // Python
            "function ", "const ", "let ", "var ",   // JavaScript  
            "public ", "private ", "void ",          // C#
            "$", "Get-", "Set-",                     // PowerShell
            "#!/bin/bash", "export ", "echo "        // Bash
        };

        var keywordCount = codeIndicators.Count(indicator => 
            content.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        // Must have multiple keywords to qualify as code (not just explanatory text)
        return keywordCount >= 2;
    }

    /// <summary>
    /// Determines if a message contains tool execution results
    /// </summary>
    private bool ContainsToolResults(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Look for tool execution indicators
        var toolIndicators = new[]
        {
            "get_current_code",
            "execute_code", 
            "save_code",
            "tool executed",
            "execution result"
        };

        return toolIndicators.Any(indicator => 
            content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    public double GetCodeQualityConfidence(string code, string language)
    {
        if (string.IsNullOrWhiteSpace(code))
            return 0.0;

        try
        {
            double confidence = 0.0;

            // Language-specific quality indicators
            confidence += GetLanguageSpecificConfidence(code, language);
            
            // General code quality indicators
            confidence += GetGeneralCodeQualityScore(code);
            
            // Completeness indicators
            confidence += GetCompletenessScore(code);

            var result = Math.Max(0.0, Math.Min(1.0, confidence));
            
            _logger.LogDebug("Code quality confidence for {Language}: {Confidence:F2}", language, result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating code quality confidence for language: {Language}", language);
            return 0.5; // Default middle confidence on error
        }
    }

    private double GetLanguageSpecificConfidence(string code, string language)
    {
        return language.ToLowerInvariant() switch
        {
            "python" => GetPythonConfidence(code),
            "c#" or "csharp" => GetCSharpConfidence(code),
            "javascript" or "js" => GetJavaScriptConfidence(code),
            "powershell" => GetPowerShellConfidence(code),
            "bash" => GetBashConfidence(code),
            _ => 0.1 // Default for unknown languages
        };
    }

    private double GetPythonConfidence(string code)
    {
        double score = 0.0;
        
        // Python-specific indicators
        if (code.Contains("def ") || code.Contains("class ")) score += 0.2;
        if (code.Contains("import ") || code.Contains("from ")) score += 0.15;
        if (Regex.IsMatch(code, @"^[a-zA-Z_][a-zA-Z0-9_]*\s*=", RegexOptions.Multiline)) score += 0.1;
        if (code.Contains("try:") && code.Contains("except")) score += 0.1;
        
        return score;
    }

    private double GetCSharpConfidence(string code)
    {
        double score = 0.0;
        
        // C#-specific indicators
        if (code.Contains("public ") || code.Contains("private ")) score += 0.2;
        if (code.Contains("class ") || code.Contains("interface ")) score += 0.15;
        if (code.Contains("using ")) score += 0.1;
        if (code.Contains("try") && code.Contains("catch")) score += 0.1;
        if (code.Contains("{") && code.Contains("}")) score += 0.1;
        
        return score;
    }

    private double GetJavaScriptConfidence(string code)
    {
        double score = 0.0;
        
        // JavaScript-specific indicators
        if (code.Contains("function ") || code.Contains("=>")) score += 0.2;
        if (code.Contains("const ") || code.Contains("let ") || code.Contains("var ")) score += 0.15;
        if (code.Contains("console.")) score += 0.1;
        if (code.Contains("try") && code.Contains("catch")) score += 0.1;
        
        return score;
    }

    private double GetPowerShellConfidence(string code)
    {
        double score = 0.0;
        
        // PowerShell-specific indicators
        if (code.Contains("$")) score += 0.2;
        if (code.Contains("Get-") || code.Contains("Set-") || code.Contains("New-")) score += 0.15;
        if (code.Contains("Write-")) score += 0.1;
        if (code.Contains("try") && code.Contains("catch")) score += 0.1;
        
        return score;
    }

    private double GetBashConfidence(string code)
    {
        double score = 0.0;
        
        // Bash-specific indicators
        if (code.StartsWith("#!/bin/bash") || code.StartsWith("#!/bin/sh")) score += 0.2;
        if (code.Contains("$") && (code.Contains("export") || code.Contains("echo"))) score += 0.15;
        if (code.Contains("if [") || code.Contains("for ") || code.Contains("while ")) score += 0.1;
        
        return score;
    }

    private double GetGeneralCodeQualityScore(string code)
    {
        double score = 0.0;
        
        // General programming patterns
        if (code.Contains("=")) score += 0.05; // Variable assignments
        if (code.Contains("(") && code.Contains(")")) score += 0.05; // Function calls
        if (code.Contains("if")) score += 0.05; // Conditional logic
        if (code.Contains("for") || code.Contains("while")) score += 0.05; // Loops
        
        // Error handling patterns
        if ((code.Contains("try") && code.Contains("catch")) || 
            (code.Contains("try:") && code.Contains("except"))) score += 0.1;
            
        return score;
    }

    private double GetCompletenessScore(string code)
    {
        double score = 0.0;
        
        // Length-based completeness (longer code is typically more complete)
        if (code.Length > 50) score += 0.05;
        if (code.Length > 200) score += 0.05;
        if (code.Length > 500) score += 0.05;
        
        // Multiple lines indicate more complete code
        var lineCount = code.Split('\n').Length;
        if (lineCount > 3) score += 0.05;
        if (lineCount > 10) score += 0.05;
        
        return score;
    }
}