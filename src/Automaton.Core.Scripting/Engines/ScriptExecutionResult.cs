namespace Automaton.Core.Scripting.Engines;

/// <summary>
/// Represents the result of script execution with structured error information
/// </summary>
public class ScriptExecutionResult
{
    /// <summary>
    /// Whether the script executed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
 /// Output variables produced by the script as JSON strings.
 /// Each value is a JSON-formatted string that consumers can deserialize as needed.
 /// </summary>
    public IDictionary<string, string> OutputVariables { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Standard output captured during execution
    /// </summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>
    /// Standard error output captured during execution
    /// </summary>
    public string StandardError { get; set; } = string.Empty;

  /// <summary>
  /// Structured error information
    /// </summary>
    public ScriptErrorInfo? Error { get; set; }

    /// <summary>
    /// Language-specific metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

/// <summary>
/// Structured error information from script execution
/// </summary>
public class ScriptErrorInfo
{
    /// <summary>
    /// Error type (e.g., "SyntaxError", "RuntimeError", "Exception")
    /// </summary>
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>
    /// Main error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Full stack trace with line numbers and file information
    /// </summary>
public string StackTrace { get; set; } = string.Empty;

    /// <summary>
    /// Standard error output (for scripts like batch that use stderr)
    /// </summary>
    public string StandardError { get; set; } = string.Empty;

    /// <summary>
    /// Line number where the error occurred (if available)
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Column number where the error occurred (if available)
    /// </summary>
    public int? ColumnNumber { get; set; }

    /// <summary>
 /// The problematic code snippet (if available)
    /// </summary>
    public string? CodeSnippet { get; set; }

    /// <summary>
    /// Inner exception information (for nested errors)
    /// </summary>
    public ScriptErrorInfo? InnerError { get; set; }

    /// <summary>
    /// Additional error context specific to the scripting language
    /// </summary>
    public Dictionary<string, string> AdditionalContext { get; set; } = new Dictionary<string, string>();

    /// <summary>
 /// Gets a formatted error message suitable for AI model consumption
 /// </summary>
    public string GetFormattedErrorForAI()
    {
        var parts = new List<string>();

// Error type and message
        if (!string.IsNullOrEmpty(ErrorType))
            parts.Add($"**Error Type**: {ErrorType}");
        
        if (!string.IsNullOrEmpty(Message))
     parts.Add($"**Message**: {Message}");

        // Location information
     if (LineNumber.HasValue)
        {
            var location = $"Line {LineNumber}";
            if (ColumnNumber.HasValue)
          location += $", Column {ColumnNumber}";
        parts.Add($"**Location**: {location}");
        }

        // Code snippet
        if (!string.IsNullOrEmpty(CodeSnippet))
            parts.Add($"**Code**:\n```\n{CodeSnippet}\n```");

    // Stack trace (most important for AI)
  if (!string.IsNullOrEmpty(StackTrace))
  parts.Add($"**Stack Trace**:\n```\n{StackTrace}\n```");

        // Additional context
        if (AdditionalContext.Any())
        {
      parts.Add("**Additional Context**:");
  foreach (var (key, value) in AdditionalContext)
   parts.Add($"- {key}: {value}");
        }

 // Inner errors
        if (InnerError != null)
{
            parts.Add("\n**Caused by**:");
          parts.Add(InnerError.GetFormattedErrorForAI());
        }

        return string.Join("\n\n", parts);
    }
}