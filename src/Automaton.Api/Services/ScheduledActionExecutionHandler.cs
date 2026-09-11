using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
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
    ITrustedInfrastructureActionDeploymentService infrastructureActions,
    ILogger<ScheduledActionExecutionHandler> logger)
{
    public async Task HandleAsync(ScheduledActionExecutionRequestedEvent message, CancellationToken cancellationToken)
    {
        var snapshot = await db.ActionScheduleOccurrences.AsNoTracking()
            .Include(x => x.Schedule).SingleOrDefaultAsync(x => x.Id == message.OccurrenceId, cancellationToken);
        if (snapshot == null
            || snapshot.ScheduleId != message.ScheduleId
            || snapshot.IdempotencyKey != message.IdempotencyKey
            || snapshot.ScheduledForUtc != message.ScheduledForUtc)
        {
            logger.LogWarning("Rejected mismatched scheduled Action occurrence {OccurrenceId}", message.OccurrenceId);
            return;
        }
        if (snapshot.Status == ActionScheduleOccurrenceStatuses.Dispatching)
        {
            var existingExecution = await db.AppModuleExecutions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == snapshot.Id, cancellationToken);
            if (existingExecution != null)
            {
                await db.ActionScheduleOccurrences.Where(x => x.Id == snapshot.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ExecutionId, existingExecution.Id)
                        .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatched)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
                return;
            }
            if (snapshot.UpdatedAtUtc > DateTime.UtcNow.AddMinutes(-2)) return;
        }
        if (snapshot.Status is ActionScheduleOccurrenceStatuses.Dispatched
            or ActionScheduleOccurrenceStatuses.Skipped
            or ActionScheduleOccurrenceStatuses.Failed)
            return;

        var claimed = await db.ActionScheduleOccurrences
            .Where(x => x.Id == message.OccurrenceId
                && (x.Status == ActionScheduleOccurrenceStatuses.Published
                    || x.Status == ActionScheduleOccurrenceStatuses.Dispatching
                        && x.UpdatedAtUtc <= DateTime.UtcNow.AddMinutes(-2)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Dispatching)
                .SetProperty(x => x.Attempt, x => x.Attempt + 1)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
        if (claimed == 0) return;

        try
        {
            db.ChangeTracker.Clear();
            var occurrence = await db.ActionScheduleOccurrences.Include(x => x.Schedule)
                .SingleAsync(x => x.Id == message.OccurrenceId, cancellationToken);
            var schedule = occurrence.Schedule;
            if (!schedule.Enabled && schedule.ScheduleType != ActionScheduleTypes.OneTime)
                throw new InvalidOperationException("Schedule was disabled before dispatch.");
            await schedules.ValidateForDispatchAsync(schedule, cancellationToken);
            using var input = JsonDocument.Parse(schedule.InputJson);
            var execution = schedule.ExecutionScope switch
            {
                ActionExecutionScopes.AppRunner => await executions.ExecuteScheduledAsync(
                    schedule.ApplicationId,
                    schedule.ActionKey,
                    schedule.OwnerUserId ?? throw new InvalidOperationException("AppRunner schedules require a user owner."),
                    input.RootElement.Clone(),
                    occurrence.Id,
                    cancellationToken),
                ActionExecutionScopes.InfrastructureRunner => await DispatchInfrastructureAsync(
                    schedule, occurrence.Id, input.RootElement, cancellationToken),
                ActionExecutionScopes.Worker => throw new InvalidOperationException(
                    "No trusted Worker scheduled Action handler is registered for this Action."),
                _ => throw new InvalidOperationException("Scheduled Action execution scope is invalid.")
            };
            occurrence.ExecutionId = execution.Id;
            occurrence.Status = ActionScheduleOccurrenceStatuses.Dispatched;
            occurrence.UpdatedAtUtc = DateTime.UtcNow;
            schedule.LastExecutionId = execution.Id;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            await db.ActionScheduleOccurrences.Where(x => x.Id == message.OccurrenceId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ActionScheduleOccurrenceStatuses.Failed)
                    .SetProperty(x => x.FailureSummary, Limit(ex.Message, 2000))
                    .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
            logger.LogWarning(ex, "Scheduled Action occurrence {OccurrenceId} failed before dispatch", message.OccurrenceId);
        }
    }

    private async Task<AppModuleExecution> DispatchInfrastructureAsync(
        ActionSchedule schedule,
        Guid occurrenceId,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        if (!input.TryGetProperty("runnerHostId", out var hostIdElement)
            || !Guid.TryParse(hostIdElement.GetString(), out var hostId))
            throw new InvalidOperationException("InfrastructureRunner schedules require a server-managed runnerHostId.");
        var host = await db.RunnerHosts.Include(x => x.InfrastructureRunner)
            .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken)
            ?? throw new KeyNotFoundException("Runner Host not found.");
        var runner = host.InfrastructureRunner
            ?? throw new InvalidOperationException("Runner Host has no Infrastructure Runner.");
        if (host.Status != RunnerHostStatuses.Active
            || host.ProvisioningStatus != RunnerHostProvisioningStatuses.Ready
            || !host.ValidatedAtUtc.HasValue
            || !host.ApprovedAtUtc.HasValue
            || !string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase)
            || !RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            throw new InvalidOperationException("Infrastructure Runner is not active, approved, ready, and online.");
        var module = await infrastructureActions.EnsureDeployedAsync(
            runner.ConnectionId!, schedule.ActionKey, cancellationToken);
        var deploymentTargetId = await db.AppDeploymentTargets
            .Where(target =>
                target.WebAppId == schedule.ApplicationId &&
                target.IsDefault)
            .Select(target => target.Id)
            .SingleAsync(cancellationToken);
        return await executions.ExecuteScheduledOnRunnerAsync(
            schedule.ApplicationId,
            deploymentTargetId,
            schedule.ActionKey,
            input.Clone(),
            occurrenceId,
            runner,
            module,
            cancellationToken);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
