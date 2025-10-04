using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Utilities;

/// <summary>
/// Utility class for extracting and parsing code content from various text formats
/// </summary>
public static class CodeExtractionUtility
{
    /// <summary>
    /// Extracts code content from a tool result that may be JSON or contain markdown code blocks
    /// </summary>
    /// <param name="toolResult">The raw tool result content</param>
    /// <returns>The extracted code content, or empty string if none found</returns>
    public static string ExtractCodeFromResult(string toolResult)
    {
        if (string.IsNullOrWhiteSpace(toolResult))
            return string.Empty;

        // First try to parse as JSON and look for a "code" property
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            if (doc.RootElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // If JSON parsing fails, try to extract from markdown code blocks
        }

        // Try to extract from markdown code blocks
        return ExtractCodeFromMarkdown(toolResult);
    }

    /// <summary>
    /// Extracts only the code content for editor application from AI response
    /// </summary>
    /// <param name="response">The AI response containing both explanation and code</param>
    /// <param name="language">The programming language for code block identification</param>
    /// <returns>Only the code content, or empty string if none found</returns>
    public static string ExtractCodeForEditor(string response, string language)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var languageId = language?.ToLowerInvariant() ?? "python";
        
        // Look for code blocks with language identifiers (more precise pattern)
        var codeBlockPattern = $@"```{Regex.Escape(languageId)}\s*\n([\s\S]*?)\n```|```\s*\n([\s\S]*?)\n```";
        var matches = Regex.Matches(response, codeBlockPattern, RegexOptions.IgnoreCase);

        if (matches.Count > 0)
        {
            // Take the last code block (most likely to be the final code)
            var lastMatch = matches[matches.Count - 1];
            
            // Extract the code content (from either capture group)
            var codeContent = !string.IsNullOrEmpty(lastMatch.Groups[1].Value) 
                ? lastMatch.Groups[1].Value.Trim() 
                : lastMatch.Groups[2].Value.Trim();
            
            return codeContent;
        }

        // If no code blocks found, check if the entire response looks like code
        if (IsLikelyCode(response))
        {
            return response;
        }

        return string.Empty;
    }

    /// <summary>
    /// Formats AI response for display by enhancing code blocks with HTML pre tags
    /// </summary>
    /// <param name="response">The AI response to format</param>
    /// <param name="language">The programming language for syntax highlighting class</param>
    /// <returns>HTML-formatted response with enhanced code blocks</returns>
    public static string FormatResponseForDisplay(string response, string language)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var languageId = language?.ToLowerInvariant() ?? "python";
        
        // Replace markdown code blocks with HTML pre tags
        var codeBlockPattern = $@"```{Regex.Escape(languageId)}\s*\n([\s\S]*?)\n```|```\s*\n([\s\S]*?)\n```";
        
        var result = Regex.Replace(response, codeBlockPattern, (match) =>
        {
            var codeContent = !string.IsNullOrEmpty(match.Groups[1].Value) 
                ? match.Groups[1].Value.Trim() 
                : match.Groups[2].Value.Trim();
            
            // Escape HTML in code content
            var escapedCode = System.Net.WebUtility.HtmlEncode(codeContent);
            
            return $@"<pre class=""generated-code-block""><code class=""language-{languageId}"">{escapedCode}</code></pre>";
        }, RegexOptions.IgnoreCase);

        // Only convert line breaks to HTML - leave other markdown as plain text
        result = result.Replace("\n", "<br>", StringComparison.Ordinal);

        return result;
    }

    /// <summary>
    /// Extracts code content from markdown code blocks
    /// </summary>
    /// <param name="content">Content that may contain markdown code blocks</param>
    /// <returns>The code content from the first code block found, or empty string</returns>
    private static string ExtractCodeFromMarkdown(string content)
    {
        if (!content.Contains("```"))
            return string.Empty;

        var start = content.IndexOf("```");
        var end = content.IndexOf("```", start + 3);
        
        if (end <= start)
            return string.Empty;

        var codeBlock = content.Substring(start + 3, end - start - 3);
        
        // Remove language identifier from first line if present
        var firstNewline = codeBlock.IndexOf('\n');
        if (firstNewline > 0 && firstNewline < 20)
        {
            var possibleLanguage = codeBlock.Substring(0, firstNewline).Trim();
            // If the first line looks like a language identifier, remove it
            if (possibleLanguage.Length <= 15 && !possibleLanguage.Contains(' ') && !possibleLanguage.Contains('\t'))
            {
                codeBlock = codeBlock.Substring(firstNewline + 1);
            }
        }

        return codeBlock.Trim();
    }

    /// <summary>
    /// Determines if a string is likely to be code based on common programming patterns
    /// </summary>
    /// <param name="content">The content to analyze</param>
    /// <returns>True if the content appears to be code</returns>
    private static bool IsLikelyCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var codeIndicators = new[]
        {
            "def ", "class ", "import ", "from ", // Python
            "function ", "const ", "let ", "var ", // JavaScript
            "public ", "private ", "void ", "int ", "string ", // C#
            "if ", "for ", "while ", "try ", "catch ", // General programming constructs
            "{", "}", "()", "[]", "=", ";", // Code symbols
        };

        var indicatorCount = codeIndicators.Count(indicator => 
            content.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        // Also check for code-like structure (indentation, brackets, etc.)
        var hasCodeStructure = content.Contains("    ") || // 4-space indentation
                              content.Contains("\t") ||   // Tab indentation
                              (content.Contains("{") && content.Contains("}")) ||
                              (content.Contains("(") && content.Contains(")"));

        return indicatorCount >= 3 || (indicatorCount >= 1 && hasCodeStructure);
    }

    /// <summary>
    /// Counts the number of lines in a code string
    /// </summary>
    /// <param name="code">The code content</param>
    /// <returns>Number of lines</returns>
    public static int GetCodeLineCount(string code)
    {
        if (string.IsNullOrEmpty(code)) 
            return 0;
            
        return code.Split('\n', StringSplitOptions.None).Length;
    }
}