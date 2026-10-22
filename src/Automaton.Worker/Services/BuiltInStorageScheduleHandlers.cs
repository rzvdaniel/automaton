using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Automaton.Worker.Services;

public static class BuiltInStorageScheduleActionKeys
{
    public const string HostedRunner = "reconcile-hosted-runner-storage";
    public const string RunnerHost = "reconcile-runner-host-storage";
    public const string RetiredEnforcement = "enforce-hosted-runner-storage";
}

public sealed class ReconcileRunnerHostStorageScheduledActionHandler(
    RunnerHostStorageReconciliationService reconciliation) : IWorkerScheduledActionHandler
{
    public string ActionKey => BuiltInStorageScheduleActionKeys.RunnerHost;

    public Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken) =>
        reconciliation.ReconcileAsync(cancellationToken);
}

public sealed class BuiltInStorageScheduleSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<BuiltInStorageScheduleSeeder> logger) : IHostedService
{
    private static readonly (string Key, string Name)[] Definitions =
    [
        (BuiltInStorageScheduleActionKeys.RunnerHost, "Runner Host storage reconciliation")
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        await DisableRetiredSchedulesAsync(db, now, cancellationToken);
        foreach (var (key, name) in Definitions)
        {
            var existing = await db.ActionSchedules.SingleOrDefaultAsync(x =>
                x.OwnerType == ActionScheduleOwnerTypes.System &&
                (x.SystemIdentity == key || (x.SystemIdentity == null &&
                    x.ApplicationId == InfrastructureAppTrust.PlatformAppId && x.ActionKey == key)), cancellationToken);
            if (existing != null)
            {
                if (!existing.Enabled || !existing.NextRunAtUtc.HasValue)
                {
                    existing.Enabled = true;
                    existing.NextRunAtUtc = now.AddMinutes(1);
                    existing.UpdatedAtUtc = now;
                }
                ReconcileDefinition(existing, key, name, now);
                await SaveWithDuplicateRecoveryAsync(db, key, name, now, cancellationToken);
                continue;
            }

            db.ActionSchedules.Add(new ActionSchedule
            {
                Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.System,
                SystemIdentity = key,
                ApplicationId = InfrastructureAppTrust.PlatformAppId, ActionKey = key,
                Name = name, Description = "Built-in storage telemetry reconciliation.",
                ExecutionScope = ActionExecutionScopes.Worker, ScheduleType = ActionScheduleTypes.Interval,
                IntervalSeconds = 60, TimeZone = "UTC", Enabled = true, NextRunAtUtc = now.AddMinutes(1),
                ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning,
                MisfirePolicy = ActionScheduleMisfirePolicies.Skip, InputJson = "{}",
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            await SaveWithDuplicateRecoveryAsync(db, key, name, now, cancellationToken);
            logger.LogInformation("Seeded built-in system storage schedule {ActionKey}", key);
        }
    }

    private static async Task DisableRetiredSchedulesAsync(
        ApplicationDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        string[] retiredKeys = [BuiltInStorageScheduleActionKeys.RetiredEnforcement, BuiltInStorageScheduleActionKeys.HostedRunner];
        var schedules = await db.ActionSchedules.Where(schedule =>
            schedule.OwnerType == ActionScheduleOwnerTypes.System &&
            (retiredKeys.Contains(schedule.SystemIdentity!) ||
             (schedule.SystemIdentity == null && schedule.ApplicationId == InfrastructureAppTrust.PlatformAppId &&
              retiredKeys.Contains(schedule.ActionKey))))
            .ToListAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            schedule.Enabled = false;
            schedule.NextRunAtUtc = null;
            schedule.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SaveWithDuplicateRecoveryAsync(ApplicationDbContext db, string key, string name,
        DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueSystemIdentityViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.ActionSchedules.SingleAsync(x => x.SystemIdentity == key, cancellationToken);
            EnsureEnabled(winner, now);
            ReconcileDefinition(winner, key, name, now);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void EnsureEnabled(ActionSchedule schedule, DateTime now)
    {
        if (!schedule.Enabled || !schedule.NextRunAtUtc.HasValue)
        {
            schedule.Enabled = true;
            schedule.NextRunAtUtc = now.AddMinutes(1);
            schedule.UpdatedAtUtc = now;
        }
    }

    private static bool IsUniqueSystemIdentityViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current != null; current = current.InnerException)
        {
            var message = current.Message;
            var typeName = current.GetType().Name;
            if (!message.Contains("SystemIdentity", StringComparison.OrdinalIgnoreCase) &&
                !message.Contains("IX_ActionSchedules_SystemIdentity", StringComparison.OrdinalIgnoreCase)) continue;

            if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            {
                var number = current.GetType().GetProperty("Number", BindingFlags.Public | BindingFlags.Instance)?.GetValue(current);
                if (number is int code && code == 1062) return true;
            }
        }
        return false;
    }

    private static void ReconcileDefinition(ActionSchedule schedule, string key, string name, DateTime now)
    {
        schedule.SystemIdentity = key;
        schedule.ApplicationId = InfrastructureAppTrust.PlatformAppId;
        schedule.ActionKey = key;
        schedule.Name = name;
        schedule.Description = "Built-in storage telemetry reconciliation.";
        schedule.ExecutionScope = ActionExecutionScopes.Worker;
        schedule.ScheduleType = ActionScheduleTypes.Interval;
        schedule.IntervalSeconds = 60;
        schedule.RunAtUtc = null;
        schedule.CronExpression = null;
        schedule.TimeZone = "UTC";
        schedule.ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning;
        schedule.MisfirePolicy = ActionScheduleMisfirePolicies.Skip;
        schedule.InputJson = "{}";
        schedule.UpdatedAtUtc = now;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
