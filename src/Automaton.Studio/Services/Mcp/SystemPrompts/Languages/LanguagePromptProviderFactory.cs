namespace Automaton.Studio.Services.Mcp.SystemPrompts.Languages;

/// <summary>
/// Factory for creating language-specific prompt providers.
/// </summary>
public class LanguagePromptProviderFactory
{
    private readonly Dictionary<string, ILanguagePromptProvider> _providers;

    public LanguagePromptProviderFactory()
    {
        _providers = new Dictionary<string, ILanguagePromptProvider>(StringComparer.OrdinalIgnoreCase)
        {
            { "Python", new PythonPromptProvider() },
            { "Batch", new BatchPromptProvider() }
        };
    }

    /// <summary>
    /// Get the prompt provider for a specific language.
    /// Returns Python provider as default if language not found.
    /// </summary>
    public ILanguagePromptProvider GetProvider(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return _providers["Python"];
        }

        return _providers.TryGetValue(language, out var provider) 
            ? provider 
            : _providers["Python"]; // Default to Python
    }

    /// <summary>
    /// Get all supported languages
    /// </summary>
    public IEnumerable<string> GetSupportedLanguages()
    {
        return _providers.Keys;
    }

    /// <summary>
    /// Check if a language is supported
    /// </summary>
    public bool IsSupported(string language)
    {
        return !string.IsNullOrEmpty(language) && _providers.ContainsKey(language);
    }
}
