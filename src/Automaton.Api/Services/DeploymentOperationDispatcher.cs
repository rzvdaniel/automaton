using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Services;

/// <summary>
/// Dispatches pending deployment operations on the API instance that owns the
/// assigned Runner connection. This is deployment-operation recovery, rather
/// than a general-purpose scheduler.
/// </summary>
public sealed class DeploymentOperationDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentOperationDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DeploymentOperationService>();
                await service.DispatchOwnedOperationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Deployment operation owner dispatch failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
