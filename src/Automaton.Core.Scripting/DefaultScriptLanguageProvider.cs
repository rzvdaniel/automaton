namespace Automaton.Core.Scripting;

/// <summary>
/// Default language provider.
/// </summary>
public class DefaultScriptLanguageProvider : IScriptLanguageProvider
{
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
      { "python", "text/x-python" },
      { "batch", "text/x-batch" },
      { "powershell", "text/x-powershell" },
      { "javascript", "text/javascript" }
    };

    public string GetContentType(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "text/x-python"; // Default fallback
        }

        return LanguageMap.TryGetValue(language, out var contentType) ?
            contentType :
            "text/x-python"; // Unknown language defaults to Python
    }
}
