using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public static class RetiredStorageScheduleKeys
{
    public const string HostedRunner = "reconcile-hosted-runner-storage";
    public const string RunnerHost = "reconcile-runner-host-storage";
    public const string Enforcement = "enforce-hosted-runner-storage";
}

public sealed class RetiredStorageScheduleCleanup(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DeleteRetiredSchedulesAsync(db, cancellationToken);
    }

    private static async Task DeleteRetiredSchedulesAsync(
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        string[] retiredKeys = [RetiredStorageScheduleKeys.Enforcement, RetiredStorageScheduleKeys.HostedRunner, RetiredStorageScheduleKeys.RunnerHost];
        var retiredSchedules = db.ActionSchedules.Where(schedule =>
            schedule.OwnerType == ActionScheduleOwnerTypes.System &&
            (retiredKeys.Contains(schedule.SystemIdentity!) ||
             (schedule.SystemIdentity == null && schedule.ApplicationId == InfrastructureAppTrust.PlatformAppId &&
              retiredKeys.Contains(schedule.ActionKey))));

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (db.Database.IsRelational())
        {
            var retiredScheduleIds = retiredSchedules.Select(schedule => schedule.Id);
            await retiredSchedules.ExecuteUpdateAsync(setters => setters
                .SetProperty(schedule => schedule.Enabled, false)
                .SetProperty(schedule => schedule.NextRunAtUtc, (DateTime?)null), cancellationToken);

            var terminalOccurrences = db.ActionScheduleOccurrences
                .Where(occurrence => retiredScheduleIds.Contains(occurrence.ScheduleId))
                .Where(occurrence => occurrence.Status != ActionScheduleOccurrenceStatuses.Queued
                    && occurrence.Status != ActionScheduleOccurrenceStatuses.Published
                    && occurrence.Status != ActionScheduleOccurrenceStatuses.Dispatching);

            await db.AppModuleExecutions
                .Where(execution => execution.ScheduleOccurrenceId.HasValue
                    && terminalOccurrences.Select(occurrence => occurrence.Id)
                        .Contains(execution.ScheduleOccurrenceId.Value)
                    && execution.Status != AppModuleExecutionStatuses.Created
                    && execution.Status != AppModuleExecutionStatuses.Dispatching
                    && execution.Status != AppModuleExecutionStatuses.Running)
                .ExecuteDeleteAsync(cancellationToken);
            await terminalOccurrences
                .Where(occurrence => !db.AppModuleExecutions.Any(execution =>
                    execution.ScheduleOccurrenceId == occurrence.Id
                    && (execution.Status == AppModuleExecutionStatuses.Created
                        || execution.Status == AppModuleExecutionStatuses.Dispatching
                        || execution.Status == AppModuleExecutionStatuses.Running)))
                .ExecuteDeleteAsync(cancellationToken);
            await retiredSchedules
                .Where(schedule => !db.ActionScheduleOccurrences.Any(occurrence => occurrence.ScheduleId == schedule.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var schedules = await retiredSchedules.ToListAsync(cancellationToken);
            var scheduleIds = schedules.Select(schedule => schedule.Id).ToArray();
            foreach (var schedule in schedules)
            {
                schedule.Enabled = false;
                schedule.NextRunAtUtc = null;
            }
            var occurrences = await db.ActionScheduleOccurrences
                .Where(occurrence => scheduleIds.Contains(occurrence.ScheduleId))
                .ToListAsync(cancellationToken);
            var occurrenceIds = occurrences.Select(occurrence => occurrence.Id).ToArray();
            var executions = await db.AppModuleExecutions
                .Where(execution => execution.ScheduleOccurrenceId.HasValue
                    && occurrenceIds.Contains(execution.ScheduleOccurrenceId.Value))
                .ToListAsync(cancellationToken);
            var activeExecutionOccurrenceIds = executions
                .Where(execution => execution.Status is AppModuleExecutionStatuses.Created
                    or AppModuleExecutionStatuses.Dispatching or AppModuleExecutionStatuses.Running)
                .Select(execution => execution.ScheduleOccurrenceId!.Value)
                .ToHashSet();
            var activeOccurrenceIds = occurrences
                .Where(occurrence => occurrence.Status is ActionScheduleOccurrenceStatuses.Queued
                    or ActionScheduleOccurrenceStatuses.Published or ActionScheduleOccurrenceStatuses.Dispatching)
                .Select(occurrence => occurrence.Id)
                .Concat(activeExecutionOccurrenceIds)
                .ToHashSet();

            db.AppModuleExecutions.RemoveRange(executions.Where(execution =>
                !activeOccurrenceIds.Contains(execution.ScheduleOccurrenceId!.Value)));
            db.ActionScheduleOccurrences.RemoveRange(occurrences.Where(occurrence =>
                !activeOccurrenceIds.Contains(occurrence.Id)));
            var removableScheduleIds = schedules
                .Where(schedule => !activeOccurrenceIds.Any(occurrenceId => occurrences
                    .Any(occurrence => occurrence.Id == occurrenceId && occurrence.ScheduleId == schedule.Id)))
                .Select(schedule => schedule.Id)
                .ToHashSet();
            db.ActionSchedules.RemoveRange(schedules.Where(schedule => removableScheduleIds.Contains(schedule.Id)));
            await db.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
