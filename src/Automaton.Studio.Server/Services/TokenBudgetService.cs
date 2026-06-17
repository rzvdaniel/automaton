using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public class TokenBudgetService : ITokenBudgetService
{
    private readonly Serilog.ILogger logger;

    public TokenBudgetService()
    {
        logger = Serilog.Log.ForContext<TokenBudgetService>();
    }

    public Task RecordUsageAsync(Guid userId, string provider, string modelId, int inputTokens, int outputTokens)
    {
        logger.Debug(
            "AI token usage observed for user {UserId}: {Provider}/{ModelId}, input={InputTokens}, output={OutputTokens}",
            userId,
            provider,
            modelId,
            inputTokens,
            outputTokens);

        return Task.CompletedTask;
    }

    public Task<string> GetEffectiveModelAsync(Guid userId, string provider, string requestedModel)
    {
        return Task.FromResult(requestedModel);
    }
}
