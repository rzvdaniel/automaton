using Automaton.Core.Scripting.Engines;
using System.Text.RegularExpressions;

namespace Automaton.Core.Scripting.ErrorParsers;

/// <summary>
/// Parser for Python script errors with detailed traceback information
/// </summary>
public class PythonErrorParser : BaseScriptErrorParser
{
    public override bool HasErrors(string output, string errorOutput)
    {
        return output.Contains("ERROR:") ||
               output.Contains("Traceback") ||
               output.Contains("Exception:") ||
               output.Contains("Error:") ||
               errorOutput.Contains("Traceback") ||
               errorOutput.Contains("Error");
    }

    public override ScriptErrorInfo? ParseError(string output, string errorOutput, IDictionary<string, object> variables)
    {
        // Check for error in variables first
        if (variables.ContainsKey("error"))
        {
            return ParseFromVariables(variables, output);
        }

        // Parse from output
        var combinedOutput = $"{output}\n{errorOutput}";
        if (!HasErrors(output, errorOutput))
            return null;

        return ParsePythonTraceback(combinedOutput);
    }

    private ScriptErrorInfo ParseFromVariables(IDictionary<string, object> variables, string output)
    {
        var error = new ScriptErrorInfo
        {
            Message = variables["error"]?.ToString() ?? "Unknown error"
        };

        if (variables.ContainsKey("stackTrace"))
        {
            error.StackTrace = variables["stackTrace"]?.ToString() ?? string.Empty;
        }

        if (variables.ContainsKey("errorOutput"))
        {
            var errorOutput = variables["errorOutput"]?.ToString() ?? string.Empty;
            EnrichFromTraceback(error, errorOutput);
        }
        else
        {
            EnrichFromTraceback(error, output);
        }

        return error;
    }

    private ScriptErrorInfo ParsePythonTraceback(string output)
    {
        var error = new ScriptErrorInfo();

        // Extract error type and message from the last line
        // Python format: "ErrorType: error message"
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var lastLine = lines.LastOrDefault(l => l.Contains(":")) ?? "";

        var errorMatch = Regex.Match(lastLine, @"^([A-Za-z]+Error|Exception):\s*(.+)$");
        if (errorMatch.Success)
        {
            error.ErrorType = errorMatch.Groups[1].Value;
            error.Message = errorMatch.Groups[2].Value.Trim();
        }
        else
        {
            error.ErrorType = "PythonError";
            error.Message = lastLine.Trim();
        }

        // Extract stack trace
        var tracebackStart = output.IndexOf("Traceback (most recent call last):");
        if (tracebackStart >= 0)
        {
            error.StackTrace = output.Substring(tracebackStart);
        }
        else
        {
            error.StackTrace = output;
        }

        // Extract line number from traceback
        // Format: File "<string>", line 5, in <module>
        error.LineNumber = ExtractLineNumber(
            output,
            @"line (?<line>\d+)",
            @"File ""<string>"", line (?<line>\d+)"
        );

        return error;
    }

    private void EnrichFromTraceback(ScriptErrorInfo error, string output)
    {
        if (string.IsNullOrEmpty(error.StackTrace))
        {
            var tracebackStart = output.IndexOf("Traceback (most recent call last):");
            if (tracebackStart >= 0)
            {
                error.StackTrace = output.Substring(tracebackStart);
            }
        }

        if (!error.LineNumber.HasValue)
        {
            error.LineNumber = ExtractLineNumber(
                output,
                @"line (?<line>\d+)",
                @"File ""<string>"", line (?<line>\d+)"
            );
        }

        // Parse error type from traceback if not already set
        if (string.IsNullOrEmpty(error.ErrorType))
        {
            var errorTypeMatch = Regex.Match(output, @"([A-Za-z]+Error|Exception):");
            if (errorTypeMatch.Success)
            {
                error.ErrorType = errorTypeMatch.Groups[1].Value;
            }
        }
    }
}