namespace Automaton.Studio.Services.Mcp.SystemPrompts;

/// <summary>
/// Interface for building AI system prompts for Step code generation
/// </summary>
public interface IStepSystemPromptBuilder
{
    /// <summary>
    /// Build a system prompt for Step code generation
    /// </summary>
    /// <param name="language">The programming language (Python, PowerShell, JavaScript, etc.)</param>
    /// <returns>A comprehensive system prompt for Step code generation</returns>
    string BuildSystemPrompt(string language);
}
