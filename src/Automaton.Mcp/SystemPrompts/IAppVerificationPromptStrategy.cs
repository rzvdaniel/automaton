namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Provides a focused verification policy block appended to app system prompts.
/// </summary>
public interface IAppVerificationPromptStrategy
{
    string Name { get; }
    string BuildPromptBlock();
}
