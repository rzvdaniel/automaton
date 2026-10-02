using Automaton.Api.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Dispatches pending deployment operations on the API instance that owns the
/// assigned Runner connection. This is deployment-operation recovery, rather
/// than a general-purpose scheduler.
/// </summary>
public sealed class DeploymentOperationDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentOperationDispatcher> logger,
    IOptions<DeploymentDispatchOptions> options) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim wakeUp = new(0, 1);

    internal void Notify()
    {
        try { wakeUp.Release(); }
        catch (SemaphoreFullException)
        {
            // A scan is already requested. Operations stay authoritative in the database.
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var activeOperations = new Dictionary<Guid, Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await StartAvailableOperationsAsync(activeOperations, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Deployment operation owner dispatch failed");
                }

                try { await wakeUp.WaitAsync(PollInterval, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
        // Observe every execution and keep its scope alive until it has stopped.
        // ExecuteAsync retains unfinished leases/packages when the API wait is cancelled.
        finally { await Task.WhenAll(activeOperations.Values); }
    }

    private async Task StartAvailableOperationsAsync(Dictionary<Guid, Task> activeOperations, CancellationToken stoppingToken)
    {
        foreach (var id in activeOperations.Where(item => item.Value.IsCompleted).Select(item => item.Key).ToList())
            activeOperations.Remove(id);
        var available = options.Value.MaximumConcurrentOperations - activeOperations.Count;
        if (available <= 0) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DeploymentOperationService>();
        var ids = await service.GetDispatchableOwnedOperationIdsAsync(available, activeOperations.Keys, stoppingToken);
        foreach (var id in ids)
            activeOperations.Add(id, ExecuteOperationAsync(id, stoppingToken));
    }

    private async Task ExecuteOperationAsync(Guid operationId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DeploymentOperationService>();
            await service.ExecuteAsync(operationId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The operation service retains unfinished durable state during API shutdown.
        }
        catch (DeploymentOperationLeaseActiveException)
        {
            // Another API instance owns a live attempt; the database claim remains authoritative.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Deployment operation {OperationId} dispatch failed", operationId);
        }
        finally { if (!stoppingToken.IsCancellationRequested) Notify(); }
    }

    public override void Dispose()
    {
        base.Dispose();
        wakeUp.Dispose();
    }
}
