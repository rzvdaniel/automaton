namespace Automaton.Api.Services;

/// <summary>Copies the host's maintenance results into the history every half minute, so a run is kept even when nobody has the page open.</summary>
public sealed class ServerMaintenanceHistoryRecorder(IServiceScopeFactory scopeFactory, ILogger<ServerMaintenanceHistoryRecorder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ServerMaintenanceHistory>().SyncAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Recording the maintenance history failed");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
