using Automaton.Core.Scripting.Engines;
using System.Text.RegularExpressions;

namespace Automaton.Core.Scripting.ErrorParsers;

/// <summary>
/// Parser for Windows Batch script errors
/// </summary>
public class BatchErrorParser : BaseScriptErrorParser
{
    public override bool HasErrors(string output, string errorOutput)
    {
        return output.Contains("ERROR:") ||
               errorOutput.Contains("ERROR:") ||
               output.Contains("The system cannot find") ||
               output.Contains("is not recognized as") ||
               output.Contains("Access is denied") ||
               output.Contains("syntax error") ||
               errorOutput.Length > 0;
    }

    public override ScriptErrorInfo? ParseError(string output, string errorOutput, IDictionary<string, object> variables)
    {
        // Check for error in variables first
        if (variables.ContainsKey("error"))
        {
            return ParseFromVariables(variables, errorOutput);
        }

        // Parse from output
        if (!HasErrors(output, errorOutput))
            return null;

        return ParseBatchError(output, errorOutput);
    }

    private ScriptErrorInfo ParseFromVariables(IDictionary<string, object> variables, string errorOutput)
    {
        var errorMessage = variables["error"]?.ToString() ?? "Unknown error";
        
        // Remove "ERROR: " prefix if present
        if (errorMessage.StartsWith("ERROR: "))
            errorMessage = errorMessage.Substring(7);

        var error = new ScriptErrorInfo
        {
            ErrorType = "BatchScriptError",
            Message = errorMessage,
            StandardError = errorOutput
        };

        // Try to extract additional context
        EnrichBatchError(error, errorOutput);

        return error;
    }

    private ScriptErrorInfo ParseBatchError(string output, string errorOutput)
    {
        var error = new ScriptErrorInfo
        {
            ErrorType = "BatchScriptError"
        };

        // Combine outputs for analysis
        var combinedOutput = $"{output}\n{errorOutput}";

        // Extract error message
        var errorLines = new List<string>();
        
        // Look for common batch error patterns
        var patterns = new[]
        {
            @"ERROR:\s*(.+)",
            @"'(.+)' is not recognized as an internal or external command",
            @"The system cannot find (.+)",
            @"Access is denied(.+)?",
            @"The syntax of the command is incorrect",
            @"(.+ File Not Found)"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(combinedOutput, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            foreach (Match match in matches)
            {
                errorLines.Add(match.Groups[1].Success ? match.Groups[1].Value.Trim() : match.Value.Trim());
            }
        }

        error.Message = errorLines.Any() 
            ? string.Join("; ", errorLines.Distinct())
            : "Batch script execution failed";

        error.StandardError = errorOutput;

        // Batch scripts don't have traditional stack traces, but we can provide context
        EnrichBatchError(error, combinedOutput);

        return error;
    }

    private void EnrichBatchError(ScriptErrorInfo error, string output)
    {
        // Extract exit code if present
        var exitCodeMatch = Regex.Match(output, @"exit code:?\s*(\d+)", RegexOptions.IgnoreCase);
        if (exitCodeMatch.Success)
        {
            error.AdditionalContext["ExitCode"] = exitCodeMatch.Groups[1].Value;
        }

        // Look for ERRORLEVEL references
        var errorLevelMatch = Regex.Match(output, @"ERRORLEVEL\s*[:=]?\s*(\d+)", RegexOptions.IgnoreCase);
        if (errorLevelMatch.Success)
        {
            error.AdditionalContext["ErrorLevel"] = errorLevelMatch.Groups[1].Value;
        }

        // Extract command that failed
        var commandMatch = Regex.Match(output, @"'([^']+)'\s+is not recognized", RegexOptions.IgnoreCase);
        if (commandMatch.Success)
        {
            error.AdditionalContext["FailedCommand"] = commandMatch.Groups[1].Value;
        }

        // Create pseudo-stack trace from error context
        var stackTraceParts = new List<string>();
        stackTraceParts.Add("Batch Script Execution Context:");
        
        if (error.AdditionalContext.ContainsKey("FailedCommand"))
            stackTraceParts.Add($"  Command: {error.AdditionalContext["FailedCommand"]}");
        
        if (error.AdditionalContext.ContainsKey("ExitCode"))
            stackTraceParts.Add($"  Exit Code: {error.AdditionalContext["ExitCode"]}");

        if (!string.IsNullOrEmpty(error.StandardError))
            stackTraceParts.Add($"  Error Output:\n{error.StandardError}");

        error.StackTrace = string.Join("\n", stackTraceParts);
    }
}