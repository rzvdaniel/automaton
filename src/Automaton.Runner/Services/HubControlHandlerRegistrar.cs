using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubControlHandlerRegistrar(IServiceProvider serviceProvider, ILogger logger)
{
    private const string PingMethod = "Ping";
    private const string ReconnectWhenIdleMethod = "ReconnectWhenIdle";
    private const string ReconnectWhenIdleAcknowledgedMethod = "ReconnectWhenIdleAcknowledged";

    public void Register(
        HubConnection connection,
        Action<string?> handleReconnectWhenIdle,
        Action<bool> setReconnecting,
        Func<bool> isReconnecting,
        Func<Exception?, string> getFriendlyErrorMessage,
        Func<Task> sendHeartbeat)
    {
        RegisterServerCommands(connection, handleReconnectWhenIdle);
        RegisterLifecycleEvents(connection, setReconnecting, isReconnecting, getFriendlyErrorMessage, sendHeartbeat);
    }

    private void RegisterServerCommands(HubConnection connection, Action<string?> handleReconnectWhenIdle)
    {
        connection.On(PingMethod, () =>
        {
            logger.Debug("Received ping from server");
            return "Pong";
        });
        connection.On<WelcomeRunnerMessage>("WelcomeRunner", message =>
        {
            var apiColor = string.IsNullOrWhiteSpace(message.ApiColor) ? "(unset)" : message.ApiColor;
            logger.Debug("Runner accepted by API instance {ApiInstanceId} color {ApiColor}",
                message.ApiInstanceId, apiColor);
            WriteLine($"✅ Accepted by API instance {message.ApiInstanceId} (color: {apiColor})");
        });
        connection.On<string>(ReconnectWhenIdleMethod, async reason =>
        {
            handleReconnectWhenIdle(reason);
            try
            {
                await connection.InvokeAsync(ReconnectWhenIdleAcknowledgedMethod, reason);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to acknowledge reconnect-when-idle request");
            }
        });
    }

    private void RegisterLifecycleEvents(
        HubConnection connection,
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
            return Task.CompletedTask;
        };

        connection.Reconnecting += error =>
        {
            setReconnecting(true);
            if (error is not null) logger.Warning(error, "Connection reconnecting due to error");
            else logger.Debug("Connection reconnecting");
            WriteLine($"🔄 Reconnecting to server: {getFriendlyErrorMessage(error)}");
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            setReconnecting(false);
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
