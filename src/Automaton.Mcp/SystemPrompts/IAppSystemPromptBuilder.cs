namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Interface for unified app system prompt builder
/// </summary>
public interface IAppSystemPromptBuilder
{
    /// <summary>
    /// Build a system prompt for the given complexity tier.
    /// Falls back to the legacy single prompt when tier is null.
    /// </summary>
    string BuildSystemPrompt(ComplexityTier? tier = null);
}
