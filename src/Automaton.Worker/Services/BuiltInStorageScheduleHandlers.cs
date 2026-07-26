using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public static class BuiltInStorageScheduleActionKeys
{
    public const string HostedRunner = "reconcile-hosted-runner-storage";
    public const string RunnerHost = "reconcile-runner-host-storage";
}

public sealed class ReconcileHostedRunnerStorageScheduledActionHandler(
    HostedRunnerStorageReconciliationService reconciliation) : IWorkerScheduledActionHandler
{
    public string ActionKey => BuiltInStorageScheduleActionKeys.HostedRunner;

    public async Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken) =>
        _ = await reconciliation.ReconcileAsync(cancellationToken, includeHostedRunners: true, includeHosts: false);
}

public sealed class ReconcileRunnerHostStorageScheduledActionHandler(
    HostedRunnerStorageReconciliationService reconciliation) : IWorkerScheduledActionHandler
{
    public string ActionKey => BuiltInStorageScheduleActionKeys.RunnerHost;

    public async Task ExecuteAsync(WorkerScheduledActionContext context, CancellationToken cancellationToken) =>
        _ = await reconciliation.ReconcileAsync(cancellationToken, includeHostedRunners: false, includeHosts: true);
}

public sealed class BuiltInStorageScheduleSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<BuiltInStorageScheduleSeeder> logger) : IHostedService
{
    private static readonly (string Key, string Name)[] Definitions =
    [
        (BuiltInStorageScheduleActionKeys.HostedRunner, "Hosted Runner storage reconciliation"),
        (BuiltInStorageScheduleActionKeys.RunnerHost, "Runner Host storage reconciliation")
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        foreach (var (key, name) in Definitions)
        {
            var existing = await db.ActionSchedules.SingleOrDefaultAsync(x =>
                x.OwnerType == ActionScheduleOwnerTypes.System &&
                x.ApplicationId == InfrastructureAppTrust.PlatformAppId && x.ActionKey == key, cancellationToken);
            if (existing != null)
            {
                if (!existing.Enabled || !existing.NextRunAtUtc.HasValue)
                {
                    existing.Enabled = true;
                    existing.NextRunAtUtc = now.AddMinutes(1);
                    existing.UpdatedAtUtc = now;
                }
                continue;
            }

            db.ActionSchedules.Add(new ActionSchedule
            {
                Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.System,
                ApplicationId = InfrastructureAppTrust.PlatformAppId, ActionKey = key,
                Name = name, Description = "Built-in storage telemetry reconciliation.",
                ExecutionScope = ActionExecutionScopes.Worker, ScheduleType = ActionScheduleTypes.Interval,
                IntervalSeconds = 60, TimeZone = "UTC", Enabled = true, NextRunAtUtc = now.AddMinutes(1),
                ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning,
                MisfirePolicy = ActionScheduleMisfirePolicies.Skip, InputJson = "{}",
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            logger.LogInformation("Seeded built-in system storage schedule {ActionKey}", key);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
