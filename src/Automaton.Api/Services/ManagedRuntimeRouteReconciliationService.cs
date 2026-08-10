namespace Automaton.Api.Services;

public sealed class ManagedRuntimeRouteReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<ManagedRuntimeRouteReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BackendRouteConfigService>()
                .RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Managed runtime route reconciliation failed during API startup");
        }
    }
}
