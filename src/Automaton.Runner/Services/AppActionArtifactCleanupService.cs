using Microsoft.Extensions.Hosting;

namespace Automaton.Runner.Console.Services;

/// <summary>Runs Action artifact retention cleanup independently of Hub reconnects.</summary>
public sealed class AppActionArtifactCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    // The Runner's identity is applied a moment after startup; keep retrying the removal of interrupted Action containers meanwhile.
    private static readonly TimeSpan InterruptedCleanupRetryDelay = TimeSpan.FromSeconds(5);
    private const int InterruptedCleanupMaxAttempts = 60;
    private readonly AppActionExecutionService executions;

    public AppActionArtifactCleanupService(AppActionExecutionService executions) => this.executions = executions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cleaned = false;
        for (var attempt = 1; !cleaned && attempt <= InterruptedCleanupMaxAttempts; attempt++)
        {
            cleaned = await executions.CleanupInterruptedActionContainersAsync(stoppingToken);
            if (!cleaned && attempt < InterruptedCleanupMaxAttempts)
                await Task.Delay(InterruptedCleanupRetryDelay, stoppingToken);
        }
        if (!cleaned)
            Serilog.Log.Warning("Gave up removing the containers of Actions interrupted by the last restart; they stay marked pending");
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
