namespace Automaton.Api.ServerCleanup;

/// <summary>Every 15 seconds queues the nightly cleanup when its time has come and executes queued runs (including "Run now").</summary>
public sealed class ServerCleanupScheduler(IServiceScopeFactory scopeFactory, ILogger<ServerCleanupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ServerCleanupService>().ProcessAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "The server cleanup pass failed; it will be tried again.");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
