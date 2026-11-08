using Automaton.Api.Config;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace Automaton.Api.Services;

/// <summary>
/// Relays a browser WebSocket to a self-hosted Runner over its SignalR connection. The same access checks as
/// ordinary tunnel requests run first; the Runner opens the socket to the app and frames flow both ways.
/// </summary>
public sealed class RuntimeTunnelWebSocketService(
    RuntimeTunnelService tunnelService,
    IHubContext<AutomatonHub> hub,
    TunnelRequestRegistry registry,
    TunnelLongLivedLimiter limiter,
    IOptions<RuntimeTunnelConfig> options,
    ILogger<RuntimeTunnelWebSocketService> logger)
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);
    private readonly RuntimeTunnelConfig config = options.Value;

    /// <summary>Returns the response to send when the upgrade is refused, or null when the socket ran and ended.</summary>
    public async Task<TunnelResponse?> ProxyAsync(
        Guid appId, Guid deploymentTargetId, HttpContext context, string path, CancellationToken cancellationToken)
    {
        var resolved = await tunnelService.ResolveAsync(appId, deploymentTargetId, context.Request, path, cancellationToken);
        if (resolved.Failure is not null) return resolved.Failure;
        if (resolved.StaticSite) return Refuse(StatusCodes.Status400BadRequest, "Static sites do not use WebSockets.");

        var runner = resolved.Runner!;
        using var lease = limiter.TryAcquire(appId, config.MaxLongLivedPerApp);
        if (lease is null)
            return Refuse(StatusCodes.Status503ServiceUnavailable, "This app has too many open streaming connections.");

        var (requestId, openTask) = registry.Register(TimeSpan.FromSeconds(config.TimeoutSeconds), cancellationToken);
        var inbound = registry.RegisterWebSocket(requestId);
        var connection = hub.Clients.Client(runner.ConnectionId!);
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await connection.SendAsync(
                AutomatonHubMethods.TunnelWebSocketOpen,
                RuntimeTunnelRequestSender.CreateWebSocketRequest(requestId, appId, deploymentTargetId, context.Request, path),
                cancellationToken);
            var opened = await openTask;
            logger.LogInformation(
                "Tunnel latency: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} RuntimePath={RuntimePath} StatusCode={StatusCode} WebSocket=true RoundTripMs={RoundTripMs} UpstreamMs={UpstreamMs} TunnelOverheadMs={TunnelOverheadMs}",
                appId, deploymentTargetId, runner.Id, path, opened.StatusCode,
                stopwatch.ElapsedMilliseconds, opened.UpstreamMs, Math.Max(0, stopwatch.ElapsedMilliseconds - opened.UpstreamMs));
            if (opened.StatusCode != StatusCodes.Status101SwitchingProtocols)
                return opened.StatusCode >= 400 ? opened : Refuse(StatusCodes.Status502BadGateway, "The app did not accept the WebSocket.");

            var subProtocol = opened.HeaderValues.TryGetValue("Sec-WebSocket-Protocol", out var values) ? values.FirstOrDefault() : null;
            using var socket = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
            await BridgeAsync(socket, requestId, inbound, connection, cancellationToken);
            return null;
        }
        catch (TimeoutException)
        {
            return Refuse(StatusCodes.Status504GatewayTimeout, "The Runner did not respond in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            logger.LogWarning(exception, "Runtime WebSocket tunnel failed for app {AppId}, runner {RunnerId}", appId, runner.Id);
            return Refuse(StatusCodes.Status502BadGateway, "The Runner runtime tunnel is unavailable.");
        }
        finally
        {
            registry.RemoveWebSocket(requestId);
        }
    }

    private async Task BridgeAsync(
        WebSocket socket, string requestId, ChannelReader<TunnelWebSocketFrame> inbound,
        IClientProxy connection, CancellationToken cancellationToken)
    {
        var idleTimeout = TimeSpan.FromSeconds(config.StreamIdleTimeoutSeconds);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(idleTimeout);

        var toRunner = ObserveAsync(() => TunnelWebSocketPump.ReadAsync(socket, requestId, async frame =>
        {
            idle.CancelAfter(idleTimeout);
            await connection.SendAsync(AutomatonHubMethods.TunnelWebSocketFrame, frame, idle.Token);
        }, idle.Token));
        var fromRunner = ObserveAsync(async () =>
        {
            await foreach (var frame in inbound.ReadAllAsync(idle.Token))
            {
                idle.CancelAfter(idleTimeout);
                if (!await TunnelWebSocketPump.WriteAsync(socket, frame, idle.Token)) break;
            }
        });

        var first = await Task.WhenAny(toRunner, fromRunner);
        // One side saw a close: give the other a moment to deliver its close frame, then end both.
        await Task.WhenAny(first == toRunner ? fromRunner : toRunner, Task.Delay(CloseGrace, cancellationToken));
        idle.Cancel();

        await ObserveAsync(() => connection.SendAsync(
            AutomatonHubMethods.TunnelWebSocketFrame,
            new TunnelWebSocketFrame { RequestId = requestId, Kind = TunnelWebSocketFrameKinds.Close, Data = [] },
            CancellationToken.None));
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
            catch (WebSocketException) { socket.Abort(); }
        }
    }

    private async Task ObserveAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ChannelClosedException)
        {
            logger.LogDebug(exception, "Tunnel WebSocket side ended");
        }
    }

    private static TunnelResponse Refuse(int statusCode, string error) => new()
    {
        StatusCode = statusCode,
        Error = error,
        BodyBytes = System.Text.Encoding.UTF8.GetBytes(error),
        HeaderValues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["text/plain; charset=utf-8"]
        }
    };
}
