namespace Automaton.Api.Services;

/// <summary>Periodically probes verified custom domains so a domain turns live (and the app's address) by itself, and drops back when it stops working.</summary>
public sealed class CustomDomainLiveCheckService(
    IServiceScopeFactory scopeFactory,
    ILogger<CustomDomainLiveCheckService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<CustomDomainService>().CheckAllLiveAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Custom domain live check failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
