namespace Automaton.Worker.Services;

public sealed class ActionSchedulePollingService(
    IServiceScopeFactory scopeFactory,
    ILogger<ActionSchedulePollingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ActionScheduleProcessor>()
                    .ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Action schedule polling failed");
            }
            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
