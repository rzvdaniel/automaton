using Microsoft.Extensions.Hosting;

namespace Automaton.Runner.Console.Services;

/// <summary>Runs Action artifact retention cleanup independently of Hub reconnects.</summary>
public sealed class AppActionArtifactCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly AppActionExecutionService executions;

    public AppActionArtifactCleanupService(AppActionExecutionService executions) => this.executions = executions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Cleanup();
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            Cleanup();
    }

    private void Cleanup()
    {
        var removed = executions.CleanupExpiredArtifacts();
        if (removed > 0)
            Serilog.Log.Debug("Removed {ArtifactDirectoryCount} expired Action execution artifact directories", removed);
    }
}
