namespace Automaton.Api.Services;

/// <summary>Once at startup, empties stored <c>host-exec</c> requests an earlier run left behind (the API restarted while one waited), so no script or input stays in the database.</summary>
public sealed class HostExecSweepService(IServiceScopeFactory scopeFactory, ILogger<HostExecSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var swept = await scope.ServiceProvider.GetRequiredService<RunnerExecutionOperationService>().SweepHostExecAsync(stoppingToken);
        if (swept > 0) logger.LogInformation("Emptied {Count} host-exec request(s) left from before the restart", swept);
    }
}
