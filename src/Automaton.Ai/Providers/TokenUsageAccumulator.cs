namespace Automaton.Ai.Providers;

/// <summary>
/// Shared additive token accumulator for multi-iteration provider calls.
/// Stores billed totals that should be emitted once on the final turn.
/// </summary>
internal sealed class TokenUsageAccumulator
{
    public int? InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int? CacheReadTokens { get; private set; }
    public int? CacheCreationTokens { get; private set; }

    public bool HasAny =>
        InputTokens.HasValue ||
        OutputTokens > 0 ||
        CacheReadTokens.HasValue ||
        CacheCreationTokens.HasValue;

    public int? OutputTokensOrNull => OutputTokens > 0 ? OutputTokens : null;

    public void Add(
        int? inputTokens,
        int? outputTokens,
        int? cacheReadTokens = null,
        int? cacheCreationTokens = null)
    {
        if (inputTokens.HasValue)
            InputTokens = (InputTokens ?? 0) + inputTokens.Value;

        if (outputTokens.HasValue)
            OutputTokens += outputTokens.Value;

        if (cacheReadTokens.HasValue)
            CacheReadTokens = (CacheReadTokens ?? 0) + cacheReadTokens.Value;

        if (cacheCreationTokens.HasValue)
            CacheCreationTokens = (CacheCreationTokens ?? 0) + cacheCreationTokens.Value;
    }
}
