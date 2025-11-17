namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Interface for building AI system prompts for different contexts
/// </summary>
public interface ISystemPromptBuilder
{
    /// <summary>
    /// Build a system prompt for Step code generation
    /// </summary>
    /// <param name="language">The programming language (Python, PowerShell, JavaScript, etc.)</param>
    /// <returns>A comprehensive system prompt for Step code generation</returns>
    string BuildStepSystemPrompt(string language);

    /// <summary>
    /// Build a system prompt for WebApp code generation
    /// </summary>
    /// <returns>A comprehensive system prompt for WebApp HTML/CSS/JS generation</returns>
    string BuildWebAppSystemPrompt();
}
