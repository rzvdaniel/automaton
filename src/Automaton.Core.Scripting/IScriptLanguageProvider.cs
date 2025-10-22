namespace Automaton.Core.Scripting;

/// <summary>
/// Language to content type mapping.
/// Extensible for new language support.
/// </summary>
public interface IScriptLanguageProvider
{
    string GetContentType(string language);
}
