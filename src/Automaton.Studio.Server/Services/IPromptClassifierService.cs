using Automaton.Mcp.SystemPrompts;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Classifies a user's initial request into a complexity tier to select the right system prompt template.
/// </summary>
public interface IPromptClassifierService
{
    /// <summary>
    /// Classify the complexity of a user's app request.
    /// Falls back to <see cref="ComplexityTier.Simple"/> on error or low confidence.
    /// </summary>
    Task<ClassificationResult> ClassifyAsync(string userMessage, string userId);
}
