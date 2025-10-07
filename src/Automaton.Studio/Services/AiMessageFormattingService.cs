using System.Text;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for formatting AI reasoning messages with code blocks
/// </summary>
public interface IAiMessageFormattingService
{
    /// <summary>
    /// Formats AI reasoning text by converting code blocks (```) into HTML pre elements
    /// </summary>
    string FormatReasoningTextWithCode(string content);
}

/// <summary>
/// Implementation of AI message formatting service
/// </summary>
public class AiMessageFormattingService : IAiMessageFormattingService
{
    // Regex pattern to match code blocks with optional language identifier
    private static readonly Regex CodeBlockRegex = new(@"```\w*\s*\n(.*?)\n```", 
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Formats AI reasoning text by converting code blocks (```) into HTML pre elements
    /// </summary>
    public string FormatReasoningTextWithCode(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;
            
        if (!content.Contains("```"))
            return System.Net.WebUtility.HtmlEncode(content);
        
        try
        {
            var result = new StringBuilder();
            int lastIndex = 0;
            var matches = CodeBlockRegex.Matches(content);
            
            foreach (Match match in matches)
            {
                // Add text before the code block
                if (match.Index > lastIndex)
                {
                    var textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                    result.Append(System.Net.WebUtility.HtmlEncode(textBefore));
                }
                
                // Add the code block using the shared CSS class
                var code = match.Groups[1].Value;
                result.Append($"<pre class='generated-code-block'><code>{System.Net.WebUtility.HtmlEncode(code)}</code></pre>");
                
                lastIndex = match.Index + match.Length;
            }
            
            // Add any remaining text after the last code block
            if (lastIndex < content.Length)
            {
                var textAfter = content.Substring(lastIndex);
                result.Append(System.Net.WebUtility.HtmlEncode(textAfter));
            }
            
            return result.ToString();
        }
        catch
        {
            // Fallback to plain text if parsing fails
            return System.Net.WebUtility.HtmlEncode(content);
        }
    }
}
