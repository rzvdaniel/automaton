using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

public class TokenBudgetService : ITokenBudgetService
{
    private readonly ApplicationDbContext _db;
    private readonly Serilog.ILogger _logger;

    public TokenBudgetService(ApplicationDbContext db)
    {
        _db = db;
        _logger = Serilog.Log.ForContext<TokenBudgetService>();
    }

    public async Task RecordUsageAsync(Guid userId, string provider, string modelId, int inputTokens, int outputTokens)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return;

        try
        {
            // Look up model pricing to compute real USD cost
            var model = await _db.AiModels
                .AsNoTracking()
                .Include(m => m.Provider)
                .FirstOrDefaultAsync(m => m.ModelId == modelId && m.Provider.Name == provider);

            var costPer1kInput  = model?.CostPer1kTokensInput  ?? 0m;
            var costPer1kOutput = model?.CostPer1kTokensOutput ?? 0m;
            var costUsd = (inputTokens  / 1000m) * costPer1kInput
                        + (outputTokens / 1000m) * costPer1kOutput;

            var now   = DateTime.UtcNow;
            var month = now.Month;
            var year  = now.Year;

            // Atomic upsert. The IF handles the lazy month reset — no separate read needed.
            await _db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO `UserTokenAllowances`
                    (`Id`, `UserId`, `MonthlyCostLimitUsd`,
                     `CurrentMonthInputTokens`, `CurrentMonthOutputTokens`, `CurrentMonthCostUsd`,
                     `CurrentMonth`, `CurrentYear`, `OverageAction`, `UpdatedAt`)
                VALUES
                    ({0}, {1}, 10.0,
                     {2}, {3}, {4},
                     {5}, {6}, 'Downgrade', {7})
                ON DUPLICATE KEY UPDATE
                    `CurrentMonthInputTokens`  = IF(`CurrentMonth` = {5} AND `CurrentYear` = {6},
                                                    `CurrentMonthInputTokens`  + {2}, {2}),
                    `CurrentMonthOutputTokens` = IF(`CurrentMonth` = {5} AND `CurrentYear` = {6},
                                                    `CurrentMonthOutputTokens` + {3}, {3}),
                    `CurrentMonthCostUsd`      = IF(`CurrentMonth` = {5} AND `CurrentYear` = {6},
                                                    `CurrentMonthCostUsd`      + {4}, {4}),
                    `CurrentMonth` = {5},
                    `CurrentYear`  = {6},
                    `UpdatedAt`    = {7}",
                Guid.NewGuid(), userId,
                inputTokens, outputTokens, costUsd,
                month, year, now);
        }
        catch (Exception ex)
        {
            // Never block the AI call because of budget recording failure
            _logger.Warning(ex, "Failed to record token usage for user {UserId}", userId);
        }
    }

    public async Task<string> GetEffectiveModelAsync(Guid userId, string provider, string requestedModel)
    {
        try
        {
            var allowance = await _db.UserTokenAllowances
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == userId);

            var limitUsd = allowance?.MonthlyCostLimitUsd ?? 10.00m;
            var action   = allowance?.OverageAction ?? OverageActions.Downgrade;

            // If month has changed the cost is effectively 0 — not over budget
            var now = DateTime.UtcNow;
            var currentCost = (allowance?.CurrentMonth == now.Month && allowance?.CurrentYear == now.Year)
                ? (allowance?.CurrentMonthCostUsd ?? 0m)
                : 0m;

            if (currentCost <= limitUsd || action != OverageActions.Downgrade)
                return requestedModel;

            // Find cheapest active model for this provider with tools + streaming
            var cheaperModel = await _db.AiModels
                .AsNoTracking()
                .Include(m => m.Provider)
                .Where(m => m.Provider.Name == provider
                         && m.IsActive
                         && m.SupportsTools
                         && m.SupportsStreaming
                         && m.ModelId != requestedModel)
                .OrderBy(m => m.CostPer1kTokensInput ?? decimal.MaxValue)
                .ThenBy(m => m.SortOrder)
                .FirstOrDefaultAsync();

            if (cheaperModel != null)
            {
                _logger.Information(
                    "User {UserId} exceeded monthly cost limit (${CurrentCost:F4}/${LimitUsd:F2}). " +
                    "Downgrading from {RequestedModel} to {CheaperModel}",
                    userId, currentCost, limitUsd, requestedModel, cheaperModel.ModelId);
                return cheaperModel.ModelId;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to check token budget for user {UserId}, using requested model", userId);
        }

        return requestedModel;
    }

    public async Task SetAllowanceAsync(Guid userId, decimal monthlyCostLimitUsd, string overageAction)
    {
        var allowance = await _db.UserTokenAllowances
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (allowance == null)
        {
            var now = DateTime.UtcNow;
            allowance = new UserTokenAllowance
            {
                Id           = Guid.NewGuid(),
                UserId       = userId,
                CurrentMonth = now.Month,
                CurrentYear  = now.Year,
                UpdatedAt    = now
            };
            _db.UserTokenAllowances.Add(allowance);
        }

        allowance.MonthlyCostLimitUsd = monthlyCostLimitUsd;
        allowance.OverageAction       = overageAction;
        allowance.UpdatedAt           = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}
