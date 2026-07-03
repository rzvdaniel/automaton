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

        if (ContainsAnyPhrase(normalized,
                "new app",
                "from scratch",
                "start from scratch",
                "create an app",
                "build an app",
                "generate an app",
                "scaffold",
                "starter",
                "blank app"))
        {
            AddReference(references, "llms/automaton-frontend-structure.md");
            AddReference(references, "llms/automaton-starter-prompts.md");
        }

        if (ContainsWord(normalized, "backend") ||
            ContainsWord(normalized, "controller") ||
            ContainsAnyPhrase(normalized,
                "backend api",
                "server api",
                "web api",
                "api endpoint",
                "rest api",
                "database",
                "persist",
                "persistence",
                "save data",
                "store data",
                "data model",
                "crud") ||
            ContainsWord(normalized, "sqlite") ||
            ContainsWord(normalized, "sql"))
        {
            AddReference(references, "llms/automaton-backend-controllers.md");
        }

        if (ContainsAnyPhrase(normalized,
                "file picker",
                "file upload") ||
            ContainsAnyWord(normalized,
                "upload",
                "uploads",
                "image",
                "images",
                "media",
                "avatar",
                "thumbnail",
                "gallery",
                "screenshot"))
        {
            AddReference(references, "llms/automaton-media-assets.md");
        }

        if (ContainsAnyPhrase(normalized,
                "add to home screen",
                "beforeinstallprompt",
                "service worker",
                "offline") ||
            ContainsAnyWord(normalized,
                "pwa",
                "installable",
                "manifest"))
        {
            AddReference(references, "llms/automaton-frontend-structure.md");
        }

        if (ContainsAnyPhrase(normalized,
                "publish issue",
                "publish problem",
                "not working after deploy",
                "fails after deploy",
                "after publishing",
                "live app",
                "public app",
                "blank screen") ||
            ContainsAnyWord(normalized,
                "deploy",
                "deployed",
                "deployment",
                "runtime",
                "runner",
                "logs",
                "production",
                "published",
                "404"))
        {
            AddReference(references, "llms/automaton-cli-deploy.md");
        }

        return references;
    }

    private static bool ContainsAnyPhrase(string text, params string[] phrases) =>
        phrases.Any(text.Contains);

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

    private static void AddReference(List<string> references, string relativePath)
    {
        if (!references.Contains(relativePath, StringComparer.Ordinal))
        {
            references.Add(relativePath);
        }
    }
}
