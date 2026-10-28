namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Keyword-based selector for loading focused app guidance chunks only when needed.
/// </summary>
public sealed class KeywordAppPromptContextSelector : IAppPromptContextSelector
{
    public IReadOnlyList<string> SelectAdditionalReferences(string? userMessage, ComplexityTier? tier = null)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return [];
        }

        var normalized = userMessage.Trim().ToLowerInvariant();
        var references = new List<string>();
        // Reserved for future routing refinements where higher tiers may opt into broader scaffolding guidance.
        _ = tier;

        if (ContainsAnyWord(normalized,
                "action", "actions", "job", "jobs", "scheduled", "schedule", "import", "export", "background"))
        {
            AddReference(references, "automaton-actions");
        }

        return references;
    }

    private static bool ContainsAnyWord(string text, params string[] words) =>
        words.Any(word => ContainsWord(text, word));

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var startsOnBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endIndex = index + word.Length;
            var endsOnBoundary = endIndex == text.Length || !char.IsLetterOrDigit(text[endIndex]);
            if (startsOnBoundary && endsOnBoundary)
            {
                return true;
            }

            index = endIndex;
        }

        return false;
    }

    private static void AddReference(List<string> references, string key)
    {
        if (!references.Contains(key, StringComparer.Ordinal))
        {
            references.Add(key);
        }
    }
}
