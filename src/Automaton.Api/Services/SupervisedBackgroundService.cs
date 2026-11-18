namespace Automaton.Api.Services;

/// <summary>
/// Runs a background service and, when it throws (for example on a database timeout), logs the error with the
/// service name and starts a fresh instance after a growing delay, so a failing service never stops the API host.
/// </summary>
public sealed class SupervisedBackgroundService<TService>(
    Func<TService> factory,
    ILogger<SupervisedBackgroundService<TService>> logger,
    TimeSpan? initialDelay = null,
    TimeSpan? maximumDelay = null) : BackgroundService
    where TService : BackgroundService
{
    private readonly TimeSpan initial = initialDelay ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan maximum = maximumDelay ?? TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = initial;
        while (!stoppingToken.IsCancellationRequested)
        {
            var service = factory();
            try
            {
                await service.StartAsync(stoppingToken);
                if (service.ExecuteTask is { } running) await running;
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background service {Service} failed; restarting in {Delay}",
                    typeof(TService).Name, delay);
            }
            finally
            {
                try { await service.StopAsync(CancellationToken.None); }
                catch (Exception) { /* already failed or stopped */ }
                service.Dispose();
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maximum.Ticks));
        }
    }
}

public static class SupervisedBackgroundServiceExtensions
{
    /// <summary>Registers a background service that is restarted with backoff when it throws.</summary>
    public static IServiceCollection AddSupervisedHostedService<TService>(this IServiceCollection services)
        where TService : BackgroundService
    {
        services.AddSingleton<IHostedService>(sp => new SupervisedBackgroundService<TService>(
            () => ActivatorUtilities.CreateInstance<TService>(sp),
            sp.GetRequiredService<ILogger<SupervisedBackgroundService<TService>>>()));
        return services;
    }
}
