using System.Text.RegularExpressions;

namespace Automaton.Studio.Services;

/// <summary>
/// Service responsible for cleaning up generated code by removing markdown formatting
/// and other artifacts that may be present in AI-generated code responses.
/// </summary>
public class AiCodeCleanupService
{
    /// <summary>
    /// Removes markdown code block syntax from generated code
    /// </summary>
    /// <param name="generatedCode">The raw generated code that may contain markdown formatting</param>
    /// <returns>Clean code without markdown formatting</returns>
    public string CleanupGeneratedCode(string generatedCode)
    {
        if (string.IsNullOrWhiteSpace(generatedCode))
            return string.Empty;

        // Try patterns in order of specificity, from most specific to most general
        
        // Pattern 1: Standard code blocks with language and newlines
        // ```python\ncode\n``` or ```python\ncode```
        var pattern1 = @"```(\w+)\s*\n(.*?)(?:\n```|```)";

        var match1 = Regex.Match(generatedCode, pattern1, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match1.Success && match1.Groups.Count > 2)
        {
            return match1.Groups[2].Value.Trim();
        }

        // Pattern 2: Code blocks without newlines but with KNOWN language
        // ```pythoncode``` (no space between language and code) - only if python is a known language
        var languageKeywords = new[] { "python", "batch", "bash", "csharp", "javascript", "java", "cpp", "c", "go", "rust", "powershell", "shell", "sql", "xml", "json" };
        foreach (var lang in languageKeywords)
        {
            var langPattern = $@"```{lang}([^`]*?)```";
            var langMatch = Regex.Match(generatedCode, langPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (langMatch.Success && langMatch.Groups.Count > 1)
            {
                return langMatch.Groups[1].Value.Trim();
            }
        }

        // Pattern 3: Code blocks with just backticks and optional whitespace/newlines
        // ```\ncode\n``` or ``` \ncode\n ``` or ```\n\ncode\n\n```
        var pattern3 = @"```\s*\n(.*?)\n\s*```";
        
        var match3 = Regex.Match(generatedCode, pattern3, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match3.Success && match3.Groups.Count > 1)
        {
            return match3.Groups[1].Value.Trim();
        }

        // Pattern 4: Code blocks with language and spaces/newlines (more flexible)
        // ``` python \ncode\n ```
        var pattern4 = @"```\s*(\w+)?\s*\n(.*?)\n\s*```";
        
        var match4 = Regex.Match(generatedCode, pattern4, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match4.Success && match4.Groups.Count > 2)
        {
            return match4.Groups[2].Value.Trim();
        }

        // Pattern 5: Simple code blocks without newlines
        // ```code``` (where code doesn't start with a language keyword)
        var pattern5 = @"```([^`]*?)```";
        var match5 = Regex.Match(generatedCode, pattern5, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match5.Success && match5.Groups.Count > 1)
        {
            return match5.Groups[1].Value.Trim();
        }

        // If no markdown patterns found, return the original code as-is
        return generatedCode.Trim();
    }
}