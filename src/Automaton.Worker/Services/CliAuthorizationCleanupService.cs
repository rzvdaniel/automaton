using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public sealed class CliAuthorizationCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<CliAuthorizationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CLI authorization cleanup failed");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var requests = await db.CliAuthorizationRequests
            .Where(x => (x.ExpiresAt <= now.AddHours(-1) && x.ApprovedAt == null)
                || (x.CompletedAt != null && x.CompletedAt <= now.AddHours(-24))
                || (x.CancelledAt != null && x.CancelledAt <= now.AddHours(-24))
                || (x.CredentialIssuedAt != null && x.CompletedAt == null && x.CredentialIssuedAt <= now.AddHours(-1)))
            .ToListAsync(cancellationToken);

        foreach (var request in requests)
        {
            if (!request.CompletedAt.HasValue && request.ApiKeyId.HasValue)
            {
                var key = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == request.ApiKeyId.Value, cancellationToken);
                if (key is not null)
                    key.IsActive = false;
            }
            db.CliAuthorizationRequests.Remove(request);
        }

        if (requests.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cleaned up {Count} stale CLI authorization requests", requests.Count);
        }
    }
}
