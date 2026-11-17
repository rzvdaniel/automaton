using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Retention;

/// <summary>What one pruning pass removed.</summary>
public sealed record RetentionReport(int Runs, int LogsExpired, int Occurrences, int AuditEvents);

/// <summary>
/// Deletes Action run records, expires their logs and deletes audit events that are past the configured retention,
/// in small batches. The newest runs per Action are always kept, so the last run shown in Studio stays correct.
/// </summary>
public sealed class RetentionPruner(ApplicationDbContext db)
{
    public const int BatchSize = 500;
    public const int MaximumBatchesPerPass = 40;

    private static readonly string[] ActiveStatuses =
        [AppModuleExecutionStatuses.Created, AppModuleExecutionStatuses.Dispatching, AppModuleExecutionStatuses.Running];
    private static readonly string[] FailedStatuses =
        [AppModuleExecutionStatuses.Failed, AppModuleExecutionStatuses.TimedOut,
         AppModuleExecutionStatuses.RunnerUnavailable, AppModuleExecutionStatuses.Lost];
    private static readonly string[] TerminalOccurrenceStatuses =
        [ActionScheduleOccurrenceStatuses.Skipped, ActionScheduleOccurrenceStatuses.Failed, ActionScheduleOccurrenceStatuses.Dispatched];

    public async Task<RetentionReport> PruneAsync(DateTime now, CancellationToken token)
    {
        var defaults = await RetentionSettings.GetDefaultsAsync(db, token);
        var (runs, logs) = await PruneRunsAsync(defaults, now, token);
        var occurrences = await PruneOccurrencesAsync(defaults, now, token);
        var audit = await PruneAuditEventsAsync(defaults, now, token);
        return new RetentionReport(runs, logs, occurrences, audit);
    }

    private async Task<(int Runs, int Logs)> PruneRunsAsync(RetentionDefaults defaults, DateTime now, CancellationToken token)
    {
        var pairs = await db.AppModuleExecutions.AsNoTracking()
            .Where(x => !ActiveStatuses.Contains(x.Status))
            .Select(x => new { x.AppId, x.ModuleKey }).Distinct().ToListAsync(token);
        var overrides = new Dictionary<Guid, EffectiveRetention>();
        int runs = 0, logs = 0;
        foreach (var pair in pairs)
        {
            if (!overrides.TryGetValue(pair.AppId, out var policy))
                overrides[pair.AppId] = policy = RetentionSettings.Resolve(
                    defaults, await RetentionSettings.GetAppOverrideAsync(db, pair.AppId, token));
            var homeCard = pair.ModuleKey == RetentionSettings.HomeCardAction;
            var keep = homeCard ? defaults.HomeCardRunsToKeep : policy.MinimumRunsPerAction;
            var keepIds = await db.AppModuleExecutions.AsNoTracking()
                .Where(x => x.AppId == pair.AppId && x.ModuleKey == pair.ModuleKey && !ActiveStatuses.Contains(x.Status))
                .OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Id).Take(keep).ToListAsync(token);
            var lastRunIds = await db.ActionSchedules.AsNoTracking()
                .Where(x => x.ApplicationId == pair.AppId && x.ActionKey == pair.ModuleKey && x.LastExecutionId != null)
                .Select(x => x.LastExecutionId!.Value).ToListAsync(token);
            keepIds.AddRange(lastRunIds);

            var successCutoff = homeCard ? now : now.AddDays(-policy.RunDays);
            var failureCutoff = now.AddDays(-(double)policy.RunDays * policy.FailedMultiplier);
            logs += await ExpireLogsAsync(pair.AppId, pair.ModuleKey, policy, now, token);
            runs += await DeleteRunsAsync(pair.AppId, pair.ModuleKey, keepIds, successCutoff, failureCutoff, token);
        }
        return (runs, logs);
    }

    private IQueryable<AppModuleExecution> Terminal(Guid appId, string key)
        => db.AppModuleExecutions.Where(x => x.AppId == appId && x.ModuleKey == key && !ActiveStatuses.Contains(x.Status));

    private async Task<int> DeleteRunsAsync(Guid appId, string key, List<Guid> keepIds, DateTime successCutoff, DateTime failureCutoff, CancellationToken token)
    {
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerPass; batch++)
        {
            var ids = await Terminal(appId, key).Where(x => !keepIds.Contains(x.Id)
                    && ((FailedStatuses.Contains(x.Status) && x.CreatedAtUtc < failureCutoff)
                        || (!FailedStatuses.Contains(x.Status) && x.CreatedAtUtc < successCutoff)))
                .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(BatchSize).ToListAsync(token);
            if (ids.Count == 0) break;
            var set = db.AppModuleExecutions.Where(x => ids.Contains(x.Id));
            total += await DeleteAsync(set, db.AppModuleExecutions, token);
            if (ids.Count < BatchSize) break;
        }
        return total;
    }

    private async Task<int> ExpireLogsAsync(Guid appId, string key, EffectiveRetention policy, DateTime now, CancellationToken token)
    {
        var successCutoff = now.AddDays(-policy.LogDays);
        var failureCutoff = now.AddDays(-(double)policy.LogDays * policy.FailedMultiplier);
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerPass; batch++)
        {
            var ids = await Terminal(appId, key).Where(x => (x.LogsAvailableUntilUtc == null || x.LogsAvailableUntilUtc > now)
                    && ((FailedStatuses.Contains(x.Status) && x.CreatedAtUtc < failureCutoff)
                        || (!FailedStatuses.Contains(x.Status) && x.CreatedAtUtc < successCutoff)))
                .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(BatchSize).ToListAsync(token);
            if (ids.Count == 0) break;
            if (db.Database.IsRelational())
                total += await db.AppModuleExecutions.Where(x => ids.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LogsAvailableUntilUtc, now), token);
            else
            {
                var rows = await db.AppModuleExecutions.Where(x => ids.Contains(x.Id)).ToListAsync(token);
                foreach (var row in rows) row.LogsAvailableUntilUtc = now;
                await db.SaveChangesAsync(token);
                total += rows.Count;
            }
            if (ids.Count < BatchSize) break;
        }
        return total;
    }

    private async Task<int> PruneOccurrencesAsync(RetentionDefaults defaults, DateTime now, CancellationToken token)
    {
        var cutoff = now.AddDays(-(double)defaults.RunDays * defaults.FailedMultiplier);
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerPass; batch++)
        {
            var ids = await db.ActionScheduleOccurrences.AsNoTracking()
                .Where(x => TerminalOccurrenceStatuses.Contains(x.Status) && x.TriggeredAtUtc < cutoff
                    && !db.AppModuleExecutions.Any(e => e.ScheduleOccurrenceId == x.Id)
                    && !db.ActionSchedules.Any(s => s.Id == x.ScheduleId && s.LastExecutionId != null && s.LastExecutionId == x.ExecutionId))
                .OrderBy(x => x.TriggeredAtUtc).Select(x => x.Id).Take(BatchSize).ToListAsync(token);
            if (ids.Count == 0) break;
            total += await DeleteAsync(db.ActionScheduleOccurrences.Where(x => ids.Contains(x.Id)), db.ActionScheduleOccurrences, token);
            if (ids.Count < BatchSize) break;
        }
        return total;
    }

    private async Task<int> PruneAuditEventsAsync(RetentionDefaults defaults, DateTime now, CancellationToken token)
    {
        var cutoff = now.AddDays(-defaults.AuditDays);
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerPass; batch++)
        {
            var ids = await db.AuditEvents.AsNoTracking()
                .Where(x => x.CreatedAtUtc < cutoff && !RetentionSettings.SecretAuditEntityTypes.Contains(x.EntityType))
                .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(BatchSize).ToListAsync(token);
            if (ids.Count == 0) break;
            total += await DeleteAsync(db.AuditEvents.Where(x => ids.Contains(x.Id)), db.AuditEvents, token);
            if (ids.Count < BatchSize) break;
        }
        return total;
    }

    private async Task<int> DeleteAsync<T>(IQueryable<T> rows, DbSet<T> set, CancellationToken token) where T : class
    {
        if (db.Database.IsRelational()) return await rows.ExecuteDeleteAsync(token);
        var list = await rows.ToListAsync(token);
        set.RemoveRange(list);
        await db.SaveChangesAsync(token);
        return list.Count;
    }
}
