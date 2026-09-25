using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Worker.Services;

public sealed class WorkerScheduledActionDispatcher(
    ApplicationDbContext db,
    ITrustedSystemActionScheduleDispatchValidator trustedSchedules,
    IEnumerable<IWorkerScheduledActionHandler> workerHandlers)
{
    public async Task DispatchAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var ids = await FindPendingOccurrenceIdsAsync(nowUtc, cancellationToken);
        foreach (var id in ids)
            await DispatchOccurrenceAsync(id, nowUtc, cancellationToken);
    }

    private Task<List<Guid>> FindPendingOccurrenceIdsAsync(DateTime nowUtc, CancellationToken cancellationToken)
        => db.ActionScheduleOccurrences.AsNoTracking()
            .Where(x => (x.Status == ActionScheduleOccurrenceStatuses.Published
                    || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                        && x.UpdatedAtUtc < nowUtc.AddMinutes(-2))
                && x.Schedule.OwnerType == ActionScheduleOwnerTypes.System
                && x.Schedule.ExecutionScope == ActionExecutionScopes.Worker)
            .OrderBy(x => x.ScheduledForUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);

    private async Task DispatchOccurrenceAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var existing = await FindExecutionAsync(id, cancellationToken);
        if (existing != null)
        {
            await ReconcileExistingExecutionAsync(id, existing, nowUtc, cancellationToken);
            return;
        }

        if (!await TryClaimOccurrenceAsync(id, nowUtc, cancellationToken)) return;
        db.ChangeTracker.Clear();
        var occurrence = await LoadOccurrenceAsync(id, cancellationToken);
        var handler = workerHandlers.SingleOrDefault(x => x.ActionKey == occurrence.Schedule.ActionKey);
        if (!await ValidateDispatchAsync(occurrence, handler, cancellationToken)) return;
        await ExecuteOccurrenceAsync(occurrence, handler!, nowUtc, cancellationToken);
    }

    private Task<AppModuleExecution?> FindExecutionAsync(Guid id, CancellationToken cancellationToken)
        => db.AppModuleExecutions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == id, cancellationToken);

    private async Task ReconcileExistingExecutionAsync(
        Guid occurrenceId,
        AppModuleExecution existing,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ambiguous = IsAmbiguous(existing.Status);
        if (ambiguous)
            await MarkExecutionLostAsync(existing.Id, nowUtc, cancellationToken);

        await MarkOccurrenceCompleteAsync(occurrenceId, existing, ambiguous, nowUtc, cancellationToken);
    }

    private static bool IsAmbiguous(string status)
        => status is not (AppModuleExecutionStatuses.Succeeded
            or AppModuleExecutionStatuses.Failed
            or AppModuleExecutionStatuses.Lost);

    private async Task MarkExecutionLostAsync(Guid executionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await db.AppModuleExecutions.Where(x => x.Id == executionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AppModuleExecutionStatuses.Lost)
                .SetProperty(x => x.FailureSummary, "Worker stopped after accepting this scheduled execution; automatic replay was suppressed.")
                .SetProperty(x => x.CompletedAtUtc, nowUtc), cancellationToken);
    }

    private async Task MarkOccurrenceCompleteAsync(
        Guid occurrenceId,
        AppModuleExecution existing,
        bool ambiguous,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var terminalStatus = existing.Status == AppModuleExecutionStatuses.Succeeded
            ? ActionScheduleOccurrenceStatuses.Dispatched
            : ActionScheduleOccurrenceStatuses.Failed;
        await db.ActionScheduleOccurrences.Where(x => x.Id == occurrenceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ExecutionId, existing.Id)
                .SetProperty(x => x.Status, terminalStatus)
                .SetProperty(x => x.FailureSummary, ambiguous
                    ? "Worker execution outcome was ambiguous; automatic replay was suppressed."
                    : existing.FailureSummary)
                .SetProperty(x => x.UpdatedAtUtc, nowUtc), cancellationToken);
    }

    private async Task<bool> TryClaimOccurrenceAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken)
        => await db.ActionScheduleOccurrences
            .Where(x => x.Id == id
                && (x.Status == ActionScheduleOccurrenceStatuses.Published
                    || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                        && x.UpdatedAtUtc < nowUtc.AddMinutes(-2)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatching)
                .SetProperty(x => x.Attempt, x => x.Attempt + 1)
                .SetProperty(x => x.UpdatedAtUtc, nowUtc), cancellationToken) == 1;

    private Task<ActionScheduleOccurrence> LoadOccurrenceAsync(Guid id, CancellationToken cancellationToken)
        => db.ActionScheduleOccurrences.Include(x => x.Schedule)
            .SingleAsync(x => x.Id == id, cancellationToken);

    private async Task<bool> ValidateDispatchAsync(
        ActionScheduleOccurrence occurrence,
        IWorkerScheduledActionHandler? handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await trustedSchedules.ValidateAsync(occurrence.Schedule, cancellationToken);
            if (occurrence.Schedule.ExecutionScope != ActionExecutionScopes.Worker)
                throw new InvalidOperationException("The scheduled Action is not a Worker Action.");
            ValidateWorkerHandler(manifest, handler);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            occurrence.Status = ActionScheduleOccurrenceStatuses.Failed;
            occurrence.FailureSummary = Bound(ex.Message, 2000);
            occurrence.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    private async Task ExecuteOccurrenceAsync(
        ActionScheduleOccurrence occurrence,
        IWorkerScheduledActionHandler handler,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var execution = CreateExecution(occurrence, nowUtc);
        db.AppModuleExecutions.Add(execution);
        occurrence.ExecutionId = execution.Id;
        occurrence.UpdatedAtUtc = nowUtc;
        occurrence.Schedule.LastExecutionId = execution.Id;
        await db.SaveChangesAsync(cancellationToken);
        await InvokeHandlerAsync(occurrence, execution, handler, cancellationToken);
        occurrence.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static AppModuleExecution CreateExecution(ActionScheduleOccurrence occurrence, DateTime nowUtc)
        => new()
        {
            Id = Guid.NewGuid(), ScheduleOccurrenceId = occurrence.Id,
            AppId = occurrence.Schedule.ApplicationId, ModuleType = AppModuleTypes.Action,
            ModuleKey = occurrence.Schedule.ActionKey, TriggerType = AppModuleTriggerKinds.Scheduled,
            Status = AppModuleExecutionStatuses.Running, CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc, CorrelationId = occurrence.IdempotencyKey
        };

    private static async Task InvokeHandlerAsync(
        ActionScheduleOccurrence occurrence,
        AppModuleExecution execution,
        IWorkerScheduledActionHandler handler,
        CancellationToken cancellationToken)
    {
        try
        {
            using var input = JsonDocument.Parse(occurrence.Schedule.InputJson);
            await handler.ExecuteAsync(new WorkerScheduledActionContext(
                execution.Id, occurrence.Id, occurrence.IdempotencyKey, input.RootElement.Clone()), cancellationToken);
            execution.Status = AppModuleExecutionStatuses.Succeeded;
            execution.CompletedAtUtc = DateTime.UtcNow;
            occurrence.Status = ActionScheduleOccurrenceStatuses.Dispatched;
        }
        catch (Exception ex)
        {
            execution.Status = AppModuleExecutionStatuses.Failed;
            execution.FailureSummary = Bound(ex.Message, 1000);
            execution.CompletedAtUtc = DateTime.UtcNow;
            occurrence.Status = ActionScheduleOccurrenceStatuses.Failed;
            occurrence.FailureSummary = Bound(ex.Message, 2000);
        }
    }

    internal static void ValidateWorkerHandler(AppActionManifest manifest, IWorkerScheduledActionHandler? handler)
    {
        if (handler == null || !string.Equals(handler.ActionKey, manifest.Metadata.Key, StringComparison.Ordinal))
            throw new InvalidOperationException("No matching trusted Worker handler is registered for this system Action.");
    }

    private static string Bound(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
