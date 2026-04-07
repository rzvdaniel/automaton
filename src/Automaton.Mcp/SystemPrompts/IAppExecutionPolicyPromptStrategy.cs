namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Provides an execution-behavior policy block appended to app system prompts.
/// </summary>
public interface IAppExecutionPolicyPromptStrategy
{
    string Name { get; }
    string BuildPromptBlock();
}
