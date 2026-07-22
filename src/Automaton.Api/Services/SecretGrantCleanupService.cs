using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class SecretGrantCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<SecretGrantCleanupService> logger) : BackgroundService
{
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
        var grants = await db.ExecutionSecretGrants
            .Where(x => x.RevokedAtUtc == null && x.ConsumedAtUtc == null && x.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        var versions = await db.SecretVersions
            .Where(x => x.RevokedAtUtc == null && x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        foreach (var grant in grants)
            grant.RevokedAtUtc = now;
        foreach (var version in versions)
            version.RevokedAtUtc = now;
        if (grants.Count > 0 || versions.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Revoked {GrantCount} expired grants and {VersionCount} expired credential versions", grants.Count, versions.Count);
        }
    }
}
