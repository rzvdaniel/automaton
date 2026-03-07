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

    public async Task RecordUsageAsync(Guid userId, int inputTokens, int outputTokens)
    {
        var total = inputTokens + outputTokens;
        if (total <= 0) return;

        try
        {
            var now = DateTime.UtcNow;
            var month = now.Month;
            var year = now.Year;

            // Atomic upsert: insert row if missing, then update counter.
            // The IF handles the lazy month reset in a single statement — no read needed.
            await _db.Database.ExecuteSqlRawAsync(@"
                INSERT INTO `UserTokenAllowances`
                    (`Id`, `UserId`, `MonthlyTokenLimit`, `CurrentMonthTokens`,
                     `CurrentMonth`, `CurrentYear`, `OverageAction`, `UpdatedAt`)
                VALUES
                    ({0}, {1}, 1000000, {2}, {3}, {4}, 'Downgrade', {5})
                ON DUPLICATE KEY UPDATE
                    `CurrentMonthTokens` = IF(`CurrentMonth` = {3} AND `CurrentYear` = {4},
                                              `CurrentMonthTokens` + {2},
                                              {2}),
                    `CurrentMonth`       = {3},
                    `CurrentYear`        = {4},
                    `UpdatedAt`          = {5}",
                Guid.NewGuid(), userId, total, month, year, now);
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

            // No row = use defaults (unlimited)
            var limit = allowance?.MonthlyTokenLimit ?? 1_000_000;
            var action = allowance?.OverageAction ?? OverageActions.Downgrade;

            // Check if current month matches; if not, counter is effectively 0
            var now = DateTime.UtcNow;
            var currentTokens = (allowance?.CurrentMonth == now.Month && allowance?.CurrentYear == now.Year)
                ? (allowance?.CurrentMonthTokens ?? 0)
                : 0;

            if (currentTokens <= limit || action != OverageActions.Downgrade)
                return requestedModel;

            // Find cheapest active model for this provider that supports tools + streaming
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
                    "User {UserId} exceeded monthly token limit ({CurrentTokens}/{Limit}). " +
                    "Downgrading from {RequestedModel} to {CheaperModel}",
                    userId, currentTokens, limit, requestedModel, cheaperModel.ModelId);
                return cheaperModel.ModelId;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to check token budget for user {UserId}, using requested model", userId);
        }

        return requestedModel;
    }

    public async Task SetAllowanceAsync(Guid userId, int monthlyTokenLimit, string overageAction)
    {
        var allowance = await _db.UserTokenAllowances
            .FirstOrDefaultAsync(a => a.UserId == userId);

        if (allowance == null)
        {
            var now = DateTime.UtcNow;
            allowance = new UserTokenAllowance
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CurrentMonth = now.Month,
                CurrentYear = now.Year,
                UpdatedAt = now
            };
            _db.UserTokenAllowances.Add(allowance);
        }

        allowance.MonthlyTokenLimit = monthlyTokenLimit;
        allowance.OverageAction = overageAction;
        allowance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}
