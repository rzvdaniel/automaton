namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Result of an LLM-based complexity classification of a user's app request.
/// </summary>
public record ClassificationResult(
    ComplexityTier Tier,
    double Confidence,
    string Rationale
);
