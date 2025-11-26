namespace Automaton.Studio.Components.AiChat.Services;

/// <summary>
/// Service for detecting if AI response content contains actual code
/// </summary>
public static class CodeDetectionService
{
    // Strong code indicators - unambiguous programming constructs
    private static readonly string[] StrongCodeIndicators = 
    {
        "def ", "class ", "import ", "from ", // Python
        "function ", "const ", "let ", "var ", // JavaScript  
        "public ", "private ", "void ", "int ", "string ", // C#
        "async ", "await ", "return ", // General async/return
        "=>", "->", // Arrow functions
        "namespace ", "using ", // C# namespaces
        "export ", "require(", // JavaScript modules
    };
    
    // Phrases that indicate conversational/explanatory text (NOT code)
    private static readonly string[] ExplanatoryPhrases = 
    {
        "the persistent failure",
        "suggests that there might be",
        "this could prevent",
        "as a workaround",
        "you can try",
        "if you have further details",
        "those could help",
        "has been successfully",
        "has been removed",
        "has been updated",
        "has been modified",
        "has been added",
        "feel free to",
        "let me know",
        "if you need",
        "you can now",
        "the code has been",
        "i've updated",
        "i have updated",
        "successfully updated",
        "successfully removed",
        "successfully added",
        "successfully modified",
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

        // MOST RELIABLE: Code block markers (```) indicate actual code
        if (response.Contains("```"))
            return true;

        // Check for explanatory text phrases first - these indicate NOT code
        var hasExplanatoryText = ExplanatoryPhrases.Any(phrase => 
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        
        // If it has explanatory phrases, it's likely a text explanation, not code
        if (hasExplanatoryText)
            return false;

        // Count strong code indicators (unambiguous programming constructs)
        var strongIndicatorCount = StrongCodeIndicators.Count(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));

        // Require at least 2 strong programming keywords to consider it code
        // This avoids false positives from casual English text
        return strongIndicatorCount >= 2;
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
            score += 0.5;

        // Count strong programming keywords
        var keywordCount = StrongCodeIndicators.Count(indicator => 
            response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        score += Math.Min(keywordCount * 0.1, 0.4); // Max 0.4 from keywords

        // Penalize explanatory text - stronger penalty
        var explanatoryCount = ExplanatoryPhrases.Count(phrase => 
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        score -= Math.Min(explanatoryCount * 0.15, 0.5); // Max -0.5 penalty

        // Ensure score is within bounds
        return Math.Max(0.0, Math.Min(1.0, score));
    }
}