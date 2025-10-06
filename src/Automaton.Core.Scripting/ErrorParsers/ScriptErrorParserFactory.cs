namespace Automaton.Core.Scripting.ErrorParsers;

/// <summary>
/// Factory for creating language-specific error parsers
/// </summary>
public class ScriptErrorParserFactory
{
    private static readonly Dictionary<string, IScriptErrorParser> _parsers = new()
    {
        ["python"] = new PythonErrorParser(),
        ["batch"] = new BatchErrorParser(),
        ["powershell"] = new BatchErrorParser(), // // TODO! Implement true Javascript parser! Similar error patterns to batch
        ["javascript"] = new PythonErrorParser(), // // TODO! Implement true Javascript parser. Use Python parser as fallback for now
        ["js"] = new PythonErrorParser() // TODO! Implement true Javascript parser
    };

    /// <summary>
    /// Get error parser for the specified language
    /// </summary>
    public static IScriptErrorParser GetParser(string language)
    {
        var key = language.ToLowerInvariant();
        return _parsers.TryGetValue(key, out var parser) 
            ? parser 
            : new PythonErrorParser(); // Default fallback
    }

    /// <summary>
    /// Register a custom error parser for a language
    /// </summary>
    public static void RegisterParser(string language, IScriptErrorParser parser)
    {
        _parsers[language.ToLowerInvariant()] = parser;
    }
}