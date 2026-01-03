namespace Automaton.Studio.Services.Mcp.SystemPrompts;

/// <summary>
/// Interface for unified app system prompt builder
/// </summary>
public interface IAppSystemPromptBuilder
{
    /// <summary>
    /// Build a unified system prompt for combined frontend + backend generation
    /// </summary>
    string BuildSystemPrompt();
}
