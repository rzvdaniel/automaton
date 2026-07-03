namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Selects focused prompt guidance chunks to append for a specific app-editing request.
/// </summary>
public interface IAppPromptContextSelector
{
    IReadOnlyList<string> SelectAdditionalReferences(string? userMessage, ComplexityTier? tier = null);
}
