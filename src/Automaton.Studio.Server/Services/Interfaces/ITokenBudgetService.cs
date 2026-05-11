namespace Automaton.Studio.Server.Services.Interfaces;

public interface ITokenBudgetService
{
    /// <summary>
    /// Records token usage for a user. Looks up model pricing to compute real USD cost.
    /// Atomically increments monthly counters; resets them if the month has changed.
    /// Upserts the allowance row if it doesn't exist yet.
    /// </summary>
    Task RecordUsageAsync(Guid userId, string provider, string modelId, int inputTokens, int outputTokens);

    /// <summary>
    /// Returns the model to use for the given request.
    /// If the user's monthly cost exceeds their limit, returns the cheapest active model
    /// for the provider that supports tools + streaming. Otherwise returns requestedModel.
    /// </summary>
    Task<string> GetEffectiveModelAsync(Guid userId, string provider, string requestedModel);
}
