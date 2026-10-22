using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Worker.Services;

public sealed class RunnerHostStorageReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageTelemetryOptions> telemetryOptions)
{
    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(30, telemetryOptions.Value.FreshAfterMinutes));
        await MarkStaleHostsAsync(db, cutoff, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task MarkStaleHostsAsync(
        ApplicationDbContext db,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var staleHosts = await db.RunnerHosts
            .Where(x => x.StorageMeasuredAtUtc != null && x.StorageMeasuredAtUtc < cutoff)
            .ToListAsync(cancellationToken);
        foreach (var host in staleHosts)
        {
            host.StorageMeasurementStatus = StorageMeasurementStatuses.Stale;
            host.StorageState = RunnerHostStorageStates.Stale;
        }
    }

}
