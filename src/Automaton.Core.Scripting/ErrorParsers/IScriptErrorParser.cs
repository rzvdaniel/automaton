using Automaton.Core.Scripting.Engines;
using System.Text.RegularExpressions;

namespace Automaton.Core.Scripting.ErrorParsers;

/// <summary>
/// Interface for parsing language-specific error information from script output
/// </summary>
public interface IScriptErrorParser
{
    /// <summary>
    /// Parse error information from script execution output
    /// </summary>
    ScriptErrorInfo? ParseError(string output, string errorOutput, IDictionary<string, object> variables);

    /// <summary>
    /// Check if the output contains error indicators
    /// </summary>
    bool HasErrors(string output, string errorOutput);
}

/// <summary>
/// Base implementation with common error parsing logic
/// </summary>
public abstract class BaseScriptErrorParser : IScriptErrorParser
{
    public abstract ScriptErrorInfo? ParseError(string output, string errorOutput, IDictionary<string, object> variables);
    public abstract bool HasErrors(string output, string errorOutput);

    protected int? ExtractLineNumber(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["line"].Value, out var lineNum))
                return lineNum;
        }
        return null;
    }

    protected string ExtractCodeSnippet(string code, int? lineNumber, int contextLines = 2)
    {
        if (!lineNumber.HasValue || string.IsNullOrEmpty(code))
            return string.Empty;

        var lines = code.Split('\n');
        var targetLine = lineNumber.Value - 1; // 0-based index
        
        if (targetLine < 0 || targetLine >= lines.Length)
            return string.Empty;

        var start = Math.Max(0, targetLine - contextLines);
        var end = Math.Min(lines.Length - 1, targetLine + contextLines);

        var snippet = new List<string>();
        for (int i = start; i <= end; i++)
        {
            var prefix = i == targetLine ? ">>> " : "    ";
            snippet.Add($"{prefix}{i + 1}: {lines[i]}");
        }

        return string.Join("\n", snippet);
    }
}