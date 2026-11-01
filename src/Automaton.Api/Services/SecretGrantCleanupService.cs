using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class SecretGrantCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SecretGrantCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private const int MaximumBatchesPerCycle = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var grantCount = await RevokeExpiredGrantsAsync(db, now, cancellationToken);
        var versionCount = await RevokeExpiredVersionsAsync(db, now, cancellationToken);
        var auditCount = await SecretAuditLog.RemoveExpiredAsync(db, now, cancellationToken);
        if (auditCount > 0)
            logger.LogDebug("Removed {AuditCount} secret audit entries past retention", auditCount);
        if (grantCount > 0 || versionCount > 0)
        {
            logger.LogDebug(
                "Revoked {GrantCount} expired grants and {VersionCount} expired credential versions",
                grantCount,
                versionCount);
        }
    }

    private async Task<int> RevokeExpiredGrantsAsync(
        ApplicationDbContext db,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerCycle; batch++)
        {
            var ids = await db.ExecutionSecretGrants
                .AsNoTracking()
                .Where(x => x.RevokedAtUtc == null && x.ConsumedAtUtc == null && x.ExpiresAtUtc <= now)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(BatchSize)
                .ToArrayAsync(cancellationToken);
            if (ids.Length == 0)
                break;

            if (db.Database.IsRelational())
            {
                total += await db.ExecutionSecretGrants
                    .Where(x => ids.Contains(x.Id)
                        && x.RevokedAtUtc == null
                        && x.ConsumedAtUtc == null
                        && x.ExpiresAtUtc <= now)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.RevokedAtUtc, now),
                        cancellationToken);
            }
            else
            {
                var grants = await db.ExecutionSecretGrants.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
                foreach (var grant in grants)
                    grant.RevokedAtUtc = now;
                total += grants.Count;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (ids.Length < BatchSize)
                break;
            if (batch == MaximumBatchesPerCycle - 1)
                logger.LogWarning("Expired secret grant cleanup reached the per-cycle limit of {Limit}", BatchSize * MaximumBatchesPerCycle);
        }
        return total;
    }

    private async Task<int> RevokeExpiredVersionsAsync(
        ApplicationDbContext db,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var total = 0;
        for (var batch = 0; batch < MaximumBatchesPerCycle; batch++)
        {
            var ids = await db.SecretVersions
                .AsNoTracking()
                .Where(x => x.RevokedAtUtc == null && x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(BatchSize)
                .ToArrayAsync(cancellationToken);
            if (ids.Length == 0)
                break;

            if (db.Database.IsRelational())
            {
                total += await db.SecretVersions
                    .Where(x => ids.Contains(x.Id)
                        && x.RevokedAtUtc == null
                        && x.ExpiresAtUtc != null
                        && x.ExpiresAtUtc <= now)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.RevokedAtUtc, now),
                        cancellationToken);
            }
            else
            {
                var versions = await db.SecretVersions.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
                foreach (var version in versions)
                    version.RevokedAtUtc = now;
                total += versions.Count;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (ids.Length < BatchSize)
                break;
            if (batch == MaximumBatchesPerCycle - 1)
                logger.LogWarning("Expired secret version cleanup reached the per-cycle limit of {Limit}", BatchSize * MaximumBatchesPerCycle);
        }
        return total;
    }
}
