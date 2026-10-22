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
        await DisableRetiredSchedulesAsync(db, DateTime.UtcNow, cancellationToken);
    }

    private static async Task DisableRetiredSchedulesAsync(
        ApplicationDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        string[] retiredKeys = [RetiredStorageScheduleKeys.Enforcement, RetiredStorageScheduleKeys.HostedRunner, RetiredStorageScheduleKeys.RunnerHost];
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
