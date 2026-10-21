using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Services;

public sealed class RunnerHostProvisioningReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<RunnerHostProvisioningReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Runner host provisioning reconciliation failed");
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var operations = await db.RunnerHostProvisioningOperations
            .Include(x => x.RunnerHost)
            .Where(x => x.Status == RunnerHostProvisioningOperationStatuses.AwaitingInfrastructureRunner
                && x.NextTimeoutAtUtc != null
                && x.NextTimeoutAtUtc <= now)
            .ToListAsync(cancellationToken);
        foreach (var operation in operations)
        {
            operation.Status = RunnerHostProvisioningOperationStatuses.Failed;
            operation.FailureCode = "InfrastructureRunnerEnrollmentTimedOut";
            operation.Error = "The Infrastructure Runner did not enroll before the enrollment deadline.";
            operation.FinishedAtUtc = now;
            operation.RunnerHost.ProvisioningStatus = RunnerHostProvisioningStatuses.Failed;
            operation.RunnerHost.ProvisioningFailureCode = operation.FailureCode;
            operation.RunnerHost.ProvisioningFailureMessage = operation.Error;
        }
        if (operations.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Marked {Count} runner host provisioning operations as enrollment timed out", operations.Count);
        }

        var staleClaims = await db.RunnerHostProvisioningOperations
            .Where(x => x.Status == RunnerHostProvisioningOperationStatuses.Running
                && x.ClaimedUntilUtc != null
                && x.ClaimedUntilUtc <= now)
            .ToListAsync(cancellationToken);
        foreach (var operation in staleClaims)
        {
            operation.Status = RunnerHostProvisioningOperationStatuses.Pending;
            operation.ClaimedUntilUtc = null;
        }
        if (staleClaims.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Released {Count} stale runner host provisioning claims", staleClaims.Count);
        }
    }
}
