namespace Automaton.Studio.Pages.StepDesigner.Components.Services;

/// <summary>
/// Service for detecting if AI response content contains actual code
/// </summary>
public static class CodeDetectionService
{
    private static readonly string[] CodeIndicators = 
    {
        "def ", "class ", "import ", "from ", // Python
        "function ", "const ", "let ", "var ", // JavaScript
        "public ", "private ", "void ", "int ", "string ", // C#
        "if ", "for ", "while ", "try ", "catch ", // General programming constructs
        "{", "}", "[", "]", "(", ")", "=", ";", // Code symbols
    };

    private static readonly string[] ExplanatoryPhrases = 
    {
        "the persistent failure",
        "suggests that there might be",
        "this could prevent",
        "as a workaround",
        "you can try",
        "if you have further details",
        "those could help"
    };

    /// <summary>
    /// Determines if the response contains actual code or just explanatory text
    /// </summary>
    /// <param name="response">The AI response to analyze</param>
    /// <returns>True if the response appears to contain code, false otherwise</returns>
    public static bool IsActualCode(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        // Check for code block markers (most reliable indicator)
        if (response.Contains("```"))
            return true;

        // Check for specific programming language keywords
        if (response.Contains("def ") || response.Contains("import "))
            return true;

        // Check for actual code structure (not just explanatory text)
        var hasCodeStructure = CodeIndicators.Any(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        // Check if it's mostly explanatory text
        var hasExplanatoryText = ExplanatoryPhrases.Any(phrase => 
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        // If it has explanatory text and no clear code structure, it's probably not code
        if (hasExplanatoryText && !hasCodeStructure)
            return false;

        // Check if it has multiple programming keywords (indicates code)
        var keywordCount = CodeIndicators.Count(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        return keywordCount >= 3; // Require at least 3 programming keywords
    }

    /// <summary>
    /// Gets a confidence score (0.0 to 1.0) indicating how likely the response is to contain code
    /// </summary>
    /// <param name="response">The AI response to analyze</param>
    /// <returns>Confidence score between 0.0 (definitely not code) and 1.0 (definitely code)</returns>
    public static double GetCodeConfidenceScore(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0.0;

        double score = 0.0;

        // Code block markers are very strong indicators
        if (response.Contains("```"))
            score += 0.4;

        // Python-specific indicators
        if (response.Contains("def ") || response.Contains("import "))
            score += 0.3;

        // Count programming keywords
        var keywordCount = CodeIndicators.Count(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        score += Math.Min(keywordCount * 0.05, 0.3); // Max 0.3 from keywords

        // Penalize explanatory text
        var explanatoryCount = ExplanatoryPhrases.Count(phrase => 
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        score -= Math.Min(explanatoryCount * 0.1, 0.2); // Max -0.2 penalty

        // Ensure score is within bounds
        return Math.Max(0.0, Math.Min(1.0, score));
    }
}