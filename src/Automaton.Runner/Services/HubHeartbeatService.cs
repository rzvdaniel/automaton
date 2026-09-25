using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace Automaton.Runner.Console.Services;

internal sealed class HubHeartbeatService(Func<HubConnection?> connectionAccessor, ILogger logger)
{
    private const string HeartbeatMethod = "Heartbeat";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private CancellationTokenSource? tokenSource;
    private Task? heartbeatTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            if (cancellationToken.IsCancellationRequested) return;
            tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeatTask = RunAsync(tokenSource.Token);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await lifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        var currentTokenSource = tokenSource;
        var currentTask = heartbeatTask;
        tokenSource = null;
        heartbeatTask = null;
        if (currentTokenSource is null) return;

        currentTokenSource.Cancel();
        try
        {
            if (currentTask is not null) await currentTask;
        }
        finally
        {
            currentTokenSource.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await SendAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        var currentConnection = connectionAccessor();
        if (currentConnection?.State != HubConnectionState.Connected) return;
        try
        {
            await currentConnection.SendAsync(HeartbeatMethod, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Runner heartbeat failed");
        }
    }
}
