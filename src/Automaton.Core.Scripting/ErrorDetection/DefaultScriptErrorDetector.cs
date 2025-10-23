namespace Automaton.Core.Scripting.ErrorDetection;

/// <summary>
/// Default error detection strategy.
/// </summary>
public class DefaultScriptErrorDetector : IScriptErrorDetector
{
    public string? DetectError(IDictionary<string, dynamic> scriptVariables, string consoleOutput)
    {
        // Priority 1: Check for explicit error variable
        if (scriptVariables.ContainsKey("error"))
        {
            return scriptVariables["error"]?.ToString();
        }

        // Priority 2: Check for error output variable
        if (scriptVariables.ContainsKey("errorOutput"))
        {
            return scriptVariables["errorOutput"]?.ToString();
        }

        // Priority 3: Check console output for error patterns
        if (!string.IsNullOrEmpty(consoleOutput))
        {
            if (consoleOutput.Contains("ERROR:") ||
                consoleOutput.Contains("Traceback") ||
                consoleOutput.Contains("Exception:") ||
                consoleOutput.Contains("FAILED"))
            {
                return "Script execution produced errors";
            }
        }

        return null;
    }
}
