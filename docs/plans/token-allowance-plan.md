# Token Allowance System

Track per-user AI token consumption and automatically downgrade models when monthly budgets are exceeded.

## Problem

Token usage is accumulated per-session but there is no cross-session, cross-app budget tracking per user.  
No mechanism exists to switch to cheaper models when a user burns through their allowance.

## Design

### Why not query `AiChatMessage` directly?

`AiChatMessage` already has `InputTokens`/`OutputTokens`/`CreatedAt` and serves as the audit trail.  
However it has no direct `UserId` — consumption would require joining `AiChatMessages → AiChatSessions → AiChatSessionUsers` on every AI request, growing slower as history accumulates.

### Chosen approach: running counter

One row in `UserTokenAllowance` per user. No accumulation, O(1) read and check.  
`AiChatMessage` remains the source of truth for historical analysis and reconciliation.

**Concurrency** handled with a single atomic SQL UPDATE that both increments and resets the month:

```sql
UPDATE UserTokenAllowances
SET CurrentMonthTokens = IF(CurrentMonth = @month AND CurrentYear = @year,
                            CurrentMonthTokens + @delta,
                            @delta),
    CurrentMonth = @month,
    CurrentYear  = @year
WHERE UserId = @userId
```

No read-modify-write in application code — safe under concurrent requests.

## Data Model

### `UserTokenAllowance`

| Column | Type | Notes |
|--------|------|-------|
| `Id` | Guid | PK |
| `UserId` | Guid | Unique FK → AspNetUsers |
| `MonthlyTokenLimit` | int | Default: 1,000,000 |
| `CurrentMonthTokens` | int | Running counter for current month |
| `CurrentMonth` | int | 1–12; resets counter when month changes |
| `CurrentYear` | int | Resets counter when year changes |
| `OverageAction` | string | `"Downgrade"` \| `"Block"` |
| `UpdatedAt` | DateTime | Last write |

Default when no row exists: 1,000,000 tokens/month, `Downgrade` action.

## Model Downgrade Logic

`AiModel` already has `CostPer1kTokensInput/Output` and `SortOrder`.  
When `CurrentMonthTokens > MonthlyTokenLimit`:
- Query `AiModels` for cheapest active model for the same provider that supports tools + streaming
- Return that model id instead of the requested one
- If no cheaper model exists, use the requested model as-is (degrade gracefully)

## Service Interface

```csharp
public interface ITokenBudgetService
{
    // Atomically increment current month counter; upserts if no row exists
    Task RecordUsageAsync(Guid userId, int inputTokens, int outputTokens);

    // Returns requested model, or cheaper model if monthly limit exceeded
    Task<string> GetEffectiveModelAsync(Guid userId, string provider, string requestedModel);
}
```

## Integration Points

### Recording (after each AI call)

- `AiChatHistoryService.AccumulateTokensAsync` — calls `RecordUsageAsync` after updating session totals
- `AiChatHistoryService.SaveMessageAsync` — calls `RecordUsageAsync` after saving message with tokens

### Downgrade check (before each AI call)

In `AiConversationControllerBase`, after model resolution:

```csharp
model = await _tokenBudgetService.GetEffectiveModelAsync(userId, provider, model);
```

## Implementation Phases

- **Phase 1** — `UserTokenAllowance` entity + migration
- **Phase 2** — `ITokenBudgetService` + `TokenBudgetService`
- **Phase 3** — Hook recording into `AiChatHistoryService`
- **Phase 4** — Hook downgrade into `AiConversationControllerBase`
- **Phase 5** — DI registration

## Future

- `/api/token-budget/usage` endpoint returning current month usage + limit
- UI widget in app editor showing "X of Y tokens used this month"
- `OverageAction.Block` path (currently defined but not implemented — only `Downgrade` is)
- Admin panel to set per-user limits
