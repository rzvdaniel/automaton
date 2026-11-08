using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class HubExecutionHandlerRegistrar
{
    private const string TunnelRequestMethod = "TunnelRequest";
    private const string TunnelResponseMethod = "TunnelResponse";
    private readonly TunnelForwarderService tunnelForwarder;
    private readonly ILogger logger;
    private readonly RunnerTunnelSessions sessions;

    public HubExecutionHandlerRegistrar(
        TunnelForwarderService tunnelForwarder,
        ILogger logger,
        TunnelForwarderConfig? config = null)
    {
        this.tunnelForwarder = tunnelForwarder;
        this.logger = logger;
        sessions = new RunnerTunnelSessions(config ?? new TunnelForwarderConfig(), logger);
    }

    public void Register(
        HubConnection connection,
        Func<bool> tryBeginTunnelRequest,
        Action completeTunnelRequest)
    {
        // Handlers run on their own task: the client waits for a handler before reading the next message, so a
        // slow app or an open stream would otherwise hold up every other app on this Runner.
        connection.On<TunnelRequest>(TunnelRequestMethod, request =>
        {
            _ = Task.Run(() => ForwardTunnelRequestAsync(connection, request, tryBeginTunnelRequest, completeTunnelRequest));
        });
        connection.On<TunnelRequest>("TunnelWebSocketOpen", request =>
        {
            _ = Task.Run(() => OpenWebSocketAsync(connection, request, tryBeginTunnelRequest, completeTunnelRequest));
        });
        connection.On<TunnelWebSocketFrame>("TunnelWebSocketFrame", sessions.QueueFrame);
        connection.On<string>("TunnelAck", sessions.Acknowledge);
        connection.On<string>("TunnelCancel", sessions.Cancel);
    }

    private async Task OpenWebSocketAsync(
        HubConnection connection,
        TunnelRequest request,
        Func<bool> tryBeginTunnelRequest,
        Action completeTunnelRequest)
    {
        if (!tryBeginTunnelRequest())
        {
            await TrySendAsync(connection, CreateReconnectResponse(request));
            return;
        }

        try
        {
            using var slot = sessions.TryEnter(request.RuntimeAppId);
            if (slot is null)
                await TrySendAsync(connection, CreateBusyResponse(request));
            else
                await sessions.RunWebSocketAsync(connection, request);
        }
        finally
        {
            completeTunnelRequest();
        }
    }

    private async Task TrySendAsync(HubConnection connection, TunnelResponse response)
    {
        try
        {
            await connection.SendAsync(TunnelResponseMethod, response);
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "Could not return tunnel response for {RequestId}", response.RequestId);
        }
    }

    private async Task ForwardTunnelRequestAsync(
        HubConnection connection,
        TunnelRequest request,
        Func<bool> tryBeginTunnelRequest,
        Action completeTunnelRequest)
    {
        if (!tryBeginTunnelRequest())
        {
            try
            {
                await connection.SendAsync(TunnelResponseMethod, CreateReconnectResponse(request));
            }
            catch (Exception exception)
            {
                logger.Debug(exception,
                    "Could not return a reconnect response for tunnel request {RequestId}", request.RequestId);
            }
            return;
        }

        logger.Debug("Tunnel request received: {Method} {Path} RequestId={RequestId}",
            request.Method, request.Path, request.RequestId);
        try
        {
            using var slot = sessions.TryEnter(request.RuntimeAppId);
            if (slot is null)
            {
                await TrySendAsync(connection, CreateBusyResponse(request));
                return;
            }

            using var result = await tunnelForwarder.ForwardAsync(request, allowStreaming: true);
            var response = result.Response;
            if (response.StatusCode >= 200 && response.StatusCode < 300)
                logger.Debug("Tunnel response sent: {StatusCode} RequestId={RequestId}", response.StatusCode, request.RequestId);
            else
            {
                logger.Warning("Tunnel forwarded with non-2xx: {StatusCode} RequestId={RequestId} Error={Error}",
                    response.StatusCode, request.RequestId, response.Error);
                WriteLine($"⚠️ Controller error ({response.StatusCode}): {response.Error ?? "(no details)"}  [{request.Path}]");
            }
            await connection.SendAsync(TunnelResponseMethod, response);
            if (result.IsStreaming)
                await sessions.SendBodyAsync(connection, request.RequestId, result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Tunnel forwarding failed: {Method} {Path} RequestId={RequestId}",
                request.Method, request.Path, request.RequestId);
        }
        finally
        {
            completeTunnelRequest();
        }
    }

    private static TunnelResponse CreateBusyResponse(TunnelRequest request)
        => new()
        {
            RequestId = request.RequestId,
            StatusCode = 503,
            Error = "This app has too many requests in flight on the Runner."
        };

    private static TunnelResponse CreateReconnectResponse(TunnelRequest request)
        => new()
        {
            RequestId = request.RequestId,
            StatusCode = 503,
            Error = "Runner is reconnecting to the active API for runtime tunnel traffic."
        };
}
