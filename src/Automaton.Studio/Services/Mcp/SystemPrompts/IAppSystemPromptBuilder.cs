namespace Automaton.Studio.Services.Mcp.SystemPrompts;

/// <summary>
/// Interface for building AI system prompts for WebApp code generation
/// </summary>
public interface IAppSystemPromptBuilder
{
    /// <summary>
    /// Build a system prompt for WebApp code generation
    /// </summary>
    /// <returns>A comprehensive system prompt for WebApp HTML/CSS/JS generation</returns>
    string BuildSystemPrompt();
}
