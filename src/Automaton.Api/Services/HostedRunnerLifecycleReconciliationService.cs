namespace Automaton.Api.Services;

public sealed class HostedRunnerLifecycleReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<HostedRunnerLifecycleReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<HostedRunnerLifecycleReconciler>()
                    .ReconcileAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Hosted Runner lifecycle reconciliation failed"); }
        }
    }
}
