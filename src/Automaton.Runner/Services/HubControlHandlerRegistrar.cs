using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubControlHandlerRegistrar(IServiceProvider serviceProvider, ILogger logger)
{
    public void Register(
        HubConnection connection,
        Action wakeConnectionLoop,
        Action<bool> setReconnecting,
        Func<bool> isReconnecting,
        Func<Exception?, string> getFriendlyErrorMessage,
        Func<Task> sendHeartbeat)
    {
        RegisterLifecycleEvents(
            connection,
            wakeConnectionLoop,
            setReconnecting,
            isReconnecting,
            getFriendlyErrorMessage,
            sendHeartbeat);
    }

    private void RegisterLifecycleEvents(
        HubConnection connection,
        Action wakeConnectionLoop,
        Action<bool> setReconnecting,
        Func<bool> isReconnecting,
        Func<Exception?, string> getFriendlyErrorMessage,
        Func<Task> sendHeartbeat)
    {
        connection.Closed += error =>
        {
            if (error is not null)
            {
                logger.Warning(error, "Connection closed with error during operation");
                WriteLine($"⚠️ Connection lost: {getFriendlyErrorMessage(error)}");
                if (!isReconnecting()) WriteLine("🔄 Attempting to reconnect...");
            }
            else
            {
                logger.Debug("Connection closed gracefully");
                WriteLine("✅ Connection closed");
            }
            wakeConnectionLoop();
            return Task.CompletedTask;
        };

        connection.Reconnecting += error =>
        {
            setReconnecting(true);
            wakeConnectionLoop();
            if (error is not null) logger.Warning(error, "Connection reconnecting due to error");
            else logger.Debug("Connection reconnecting");
            WriteLine($"🔄 Reconnecting to server: {getFriendlyErrorMessage(error)}");
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            setReconnecting(false);
            wakeConnectionLoop();
            logger.Information("Reconnected to server with connection ID: {ConnectionId}", connectionId);
            WriteLine("✅ Reconnected to server successfully");
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var runnerHostService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
                    if (!await runnerHostService.ReRegisterAfterReconnectAsync())
                    {
                        logger.Warning("Runner re-registration after reconnect returned false");
                        WriteLine("⚠️ Runner re-registration after reconnect failed");
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Runner re-registration after reconnect failed");
                    WriteLine($"⚠️ Runner re-registration after reconnect failed: {ex.Message}");
                }
                await sendHeartbeat();
            });
            return Task.CompletedTask;
        };

    }
}
