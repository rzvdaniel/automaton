namespace Automaton.Core.Scripting.ErrorDetection;

/// <summary>
/// Strategy for detecting script execution errors.
/// Extensible for different error detection logic.
/// </summary>
public interface IScriptErrorDetector
{
    /// <summary>
    /// Detect if an error occurred during script execution.
    /// </summary>
    /// <returns>Error message if error detected, null otherwise.</returns>
    string? DetectError(IDictionary<string, dynamic> scriptVariables, string consoleOutput);
}
