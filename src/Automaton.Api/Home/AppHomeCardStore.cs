using Automaton.Core.Models;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Home;

/// <summary>Turns a finished Action run into the app's stored Home card.</summary>
public sealed class AppHomeCardStore(ApplicationDbContext db)
{
    /// <summary>
    /// A succeeded run of <c>home-card</c>, or any run with <c>homeCard</c> in its result, replaces the app's card when
    /// the card is valid. A failed <c>home-card</c> run, or a succeeded run of any Action whose <c>homeCard</c> is invalid, keeps the previous card and marks it late.
    /// </summary>
    public async Task ApplyRunAsync(Guid executionId, AppActionExecutionResult result, CancellationToken token)
    {
        var succeeded = result.Status == AppModuleExecutionStatuses.Succeeded;
        if (succeeded && result.HomeCardJson is null) return;
        var run = await db.AppModuleExecutions.AsNoTracking().Where(e => e.Id == executionId)
            .Select(e => new { e.AppId, e.ModuleKey }).SingleOrDefaultAsync(token);
        if (run is null) return;
        var isHomeCardAction = run.ModuleKey == AppHomeCardLimits.ActionKey;
        if (!succeeded && !isHomeCardAction) return;

        var check = succeeded ? AppHomeCardValidator.Check(result.HomeCardJson) : null;
        var content = check?.Card;
        var at = result.CompletedAtUtc == default ? DateTime.UtcNow : result.CompletedAtUtc;
        var row = await db.AppHomeCards.SingleOrDefaultAsync(c => c.AppId == run.AppId, token);
        if (content is null) RecordNoCard(row, run.AppId, executionId, check, at);
        else if (row is null)
        {
            db.AppHomeCards.Add(new AppHomeCard
            {
                AppId = run.AppId, CardJson = AppHomeCardValidator.Serialize(content), UpdatedAtUtc = at, ExecutionId = executionId
            });
        }
        else if (at >= row.UpdatedAtUtc)
        {
            row.CardJson = AppHomeCardValidator.Serialize(content);
            row.UpdatedAtUtc = at;
            row.ExecutionId = executionId;
            row.InvalidReason = null;
        }
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            // A parallel run stored its card first; the newer of the two wins on the next run.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Nothing valid came back: keep the previous card and mark it late; a broken card also stores why, so Studio shows the default card.</summary>
    private void RecordNoCard(AppHomeCard? row, Guid appId, Guid executionId, AppHomeCardCheck? check, DateTime at)
    {
        var reason = check is { Errors.Count: > 0 } ? AppHomeCardValidator.Reason(check.Errors) : null;
        if (row is null)
        {
            if (reason is not null)
                db.AppHomeCards.Add(new AppHomeCard { AppId = appId, CardJson = "{}", UpdatedAtUtc = at, ExecutionId = executionId, LastFailedAtUtc = at, InvalidReason = reason });
            return;
        }
        if (reason is not null && at >= row.UpdatedAtUtc) row.InvalidReason = reason;
        if (row.LastFailedAtUtc is null || row.LastFailedAtUtc < at) row.LastFailedAtUtc = at;
    }
}
