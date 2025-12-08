namespace Automaton.Studio.Services.Mcp.SystemPrompts.Languages;

/// <summary>
/// Interface for language-specific code generation prompts and templates.
/// Implementations provide guidance specific to each programming language.
/// </summary>
public interface ILanguagePromptProvider
{
    /// <summary>
    /// The programming language this provider handles
    /// </summary>
    string Language { get; }

    /// <summary>
    /// Get the code template for this language that properly handles output variables.
    /// The template should ensure outputs are JSON-serializable without extra escaping.
    /// </summary>
    string GetCodeTemplate();

    /// <summary>
    /// Get language-specific guidance for the AI about output variable handling.
    /// This should explain how to properly format outputs to avoid double-escaping issues.
    /// </summary>
    string GetOutputVariableGuidance();

    /// <summary>
    /// Get a complete example showing proper output handling for this language.
    /// </summary>
    string GetSystemMetricsExample();
}
