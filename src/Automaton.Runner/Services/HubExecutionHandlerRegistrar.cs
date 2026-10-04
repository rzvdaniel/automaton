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

    public HubExecutionHandlerRegistrar(
        TunnelForwarderService tunnelForwarder,
        ILogger logger)
    {
        this.tunnelForwarder = tunnelForwarder;
        this.logger = logger;
    }

    public void Register(
        HubConnection connection,
        Func<bool> tryBeginTunnelRequest,
        Action completeTunnelRequest)
    {
        connection.On<TunnelRequest>(TunnelRequestMethod, request =>
            ForwardTunnelRequestAsync(connection, request, tryBeginTunnelRequest, completeTunnelRequest));
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
            var response = await tunnelForwarder.ForwardAsync(request);
            if (response.StatusCode >= 200 && response.StatusCode < 300)
                logger.Debug("Tunnel response sent: {StatusCode} RequestId={RequestId}", response.StatusCode, request.RequestId);
            else
            {
                logger.Warning("Tunnel forwarded with non-2xx: {StatusCode} RequestId={RequestId} Error={Error}",
                    response.StatusCode, request.RequestId, response.Error);
                WriteLine($"⚠️ Controller error ({response.StatusCode}): {response.Error ?? "(no details)"}  [{request.Path}]");
            }
            await connection.SendAsync(TunnelResponseMethod, response);
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

    private static TunnelResponse CreateReconnectResponse(TunnelRequest request)
        => new()
        {
            RequestId = request.RequestId,
            StatusCode = 503,
            Error = "Runner is reconnecting to the active API for runtime tunnel traffic."
        };
}
