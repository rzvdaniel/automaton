using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Messaging.Events;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public sealed class ScheduledActionExecutionHandler(
    ApplicationDbContext db,
    ActionScheduleService schedules,
    AppActionExecutionService executions,
    ILogger<ScheduledActionExecutionHandler> logger)
{
    public async Task HandleAsync(ScheduledActionExecutionRequestedEvent message, CancellationToken cancellationToken)
    {
        var snapshot = await LoadOccurrenceSnapshotAsync(message.OccurrenceId, cancellationToken);
        if (!MatchesMessage(snapshot, message))
        {
            logger.LogWarning("Rejected mismatched scheduled Action occurrence {OccurrenceId}", message.OccurrenceId);
            return;
        }

        if (await RecoverDispatchAsync(snapshot!, cancellationToken))
        {
            return;
        }

        if (IsTerminal(snapshot!.Status))
            return;

        if (!await TryClaimOccurrenceAsync(message.OccurrenceId, cancellationToken))
            return;

        await DispatchClaimedOccurrenceAsync(message.OccurrenceId, cancellationToken);
    }

    private Task<ActionScheduleOccurrence?> LoadOccurrenceSnapshotAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken)
        => db.ActionScheduleOccurrences.AsNoTracking()
            .Include(x => x.Schedule)
            .SingleOrDefaultAsync(x => x.Id == occurrenceId, cancellationToken);

    private static bool MatchesMessage(
        ActionScheduleOccurrence? occurrence,
        ScheduledActionExecutionRequestedEvent message)
        => occurrence != null
           && occurrence.ScheduleId == message.ScheduleId
           && occurrence.IdempotencyKey == message.IdempotencyKey
           && occurrence.ScheduledForUtc == message.ScheduledForUtc;

    private async Task<bool> RecoverDispatchAsync(
        ActionScheduleOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        if (occurrence.Status != ActionScheduleOccurrenceStatuses.Dispatching)
            return false;

        var existingExecution = await db.AppModuleExecutions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == occurrence.Id, cancellationToken);
        if (existingExecution != null)
        {
            await MarkDispatchedAsync(occurrence.Id, existingExecution.Id, cancellationToken);
            return true;
        }

        return occurrence.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-2);
    }

    private Task<int> MarkDispatchedAsync(
        Guid occurrenceId,
        Guid executionId,
        CancellationToken cancellationToken)
        => db.ActionScheduleOccurrences.Where(x => x.Id == occurrenceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ExecutionId, executionId)
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatched)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);

    private static bool IsTerminal(string status)
        => status is ActionScheduleOccurrenceStatuses.Dispatched
            or ActionScheduleOccurrenceStatuses.Skipped
            or ActionScheduleOccurrenceStatuses.Failed;

    private async Task<bool> TryClaimOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken)
    {
        var claimed = await db.ActionScheduleOccurrences
            .Where(x => x.Id == occurrenceId
                && (x.Status == ActionScheduleOccurrenceStatuses.Published
                    || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                        && x.UpdatedAtUtc <= DateTime.UtcNow.AddMinutes(-2)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatching)
                .SetProperty(x => x.Attempt, x => x.Attempt + 1)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
        return claimed > 0;
    }

    private async Task DispatchClaimedOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            await ExecuteClaimedOccurrenceAsync(occurrenceId, cancellationToken);
        }
        catch (Exception ex)
        {
            await MarkOccurrenceFailedAsync(occurrenceId, ex, cancellationToken);
            logger.LogWarning(ex, "Scheduled Action occurrence {OccurrenceId} failed before dispatch", occurrenceId);
        }
    }

    private async Task ExecuteClaimedOccurrenceAsync(Guid occurrenceId, CancellationToken cancellationToken)
    {
        var occurrence = await db.ActionScheduleOccurrences.Include(x => x.Schedule)
            .SingleAsync(x => x.Id == occurrenceId, cancellationToken);
        var schedule = occurrence.Schedule;
        if (!schedule.Enabled && schedule.ScheduleType != ActionScheduleTypes.OneTime)
            throw new InvalidOperationException("Schedule was disabled before dispatch.");
        await schedules.ValidateForDispatchAsync(schedule, cancellationToken);
        using var input = JsonDocument.Parse(schedule.InputJson);
        var execution = await ExecuteScheduleAsync(schedule, occurrence.Id, input.RootElement, cancellationToken);
        occurrence.ExecutionId = execution.Id;
        occurrence.Status = ActionScheduleOccurrenceStatuses.Dispatched;
        occurrence.UpdatedAtUtc = DateTime.UtcNow;
        schedule.LastExecutionId = execution.Id;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<AppModuleExecution> ExecuteScheduleAsync(
        ActionSchedule schedule,
        Guid occurrenceId,
        JsonElement input,
        CancellationToken cancellationToken)
        => schedule.ExecutionScope switch
        {
            ActionExecutionScopes.AppRunner => executions.ExecuteScheduledAsync(
                schedule.ApplicationId,
                schedule.ActionKey,
                schedule.OwnerUserId ?? throw new InvalidOperationException("AppRunner schedules require a user owner."),
                input.Clone(),
                occurrenceId,
                cancellationToken),
            ActionExecutionScopes.Worker => throw new InvalidOperationException("Worker scheduled Actions are not dispatched by the Runner handler."),
            _ => throw new InvalidOperationException("Scheduled Action execution scope is invalid.")
        };

    private Task<int> MarkOccurrenceFailedAsync(
        Guid occurrenceId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return db.ActionScheduleOccurrences.Where(x => x.Id == occurrenceId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Failed)
                .SetProperty(x => x.FailureSummary, Limit(exception.Message, 2000))
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
