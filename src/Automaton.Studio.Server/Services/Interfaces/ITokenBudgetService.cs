namespace Automaton.Studio.Server.Services.Interfaces;

public interface ITokenBudgetService
{
    /// <summary>
    /// Atomically increments the current month's token counter for the user.
    /// Resets the counter if the month has changed. Upserts the row if it doesn't exist yet.
    /// </summary>
    Task RecordUsageAsync(Guid userId, int inputTokens, int outputTokens);

    /// <summary>
    /// Returns the model to use for the given request.
    /// If the user has exceeded their monthly allowance, returns the cheapest active model
    /// for the provider that supports tools + streaming. Otherwise returns requestedModel.
    /// </summary>
    Task<string> GetEffectiveModelAsync(Guid userId, string provider, string requestedModel);

    /// <summary>
    /// Updates (or creates) the allowance settings for a user.
    /// Call this when a subscription plan changes.
    /// </summary>
    Task SetAllowanceAsync(Guid userId, int monthlyTokenLimit, string overageAction);
}
