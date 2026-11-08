using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class RuntimeTunnelRequestSender(
    IHubContext<AutomatonHub> hub,
    TunnelRequestRegistry registry,
    TunnelLongLivedLimiter limiter,
    IOptions<RuntimeTunnelConfig> options,
    ILogger<RuntimeTunnelRequestSender> logger)
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Content-Length"
    };

    private const int AckEveryChunks = 8;

    private readonly RuntimeTunnelConfig config = options.Value;

    public async Task<TunnelResponse> ForwardAsync(
        Guid appId,
        Guid deploymentTargetId,
        Runner runner,
        HttpRequest request,
        string path,
        CancellationToken cancellationToken,
        bool staticSite = false)
    {
        if (request.ContentLength is > 0 && request.ContentLength > config.MaxBodyBytes)
        {
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");
        }

        var (body, bodyTooLarge) = await ReadBodyAsync(request, config.MaxBodyBytes, cancellationToken);
        if (bodyTooLarge)
        {
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");
        }

        var (requestId, responseTask) = registry.Register(
            TimeSpan.FromSeconds(config.TimeoutSeconds), cancellationToken);
        var tunnelRequest = CreateTunnelRequest(requestId, appId, deploymentTargetId, request, path, body, staticSite);

        return await SendAndWaitAsync(
            tunnelRequest, responseTask, runner, appId, deploymentTargetId, request.Host.Host, path, cancellationToken);
    }

    private async Task<TunnelResponse> SendAndWaitAsync(
        TunnelRequest tunnelRequest,
        Task<TunnelResponse> responseTask,
        Runner runner,
        Guid appId,
        Guid deploymentTargetId,
        string host,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await hub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);
            var response = await responseTask;
            LogLatency(appId, deploymentTargetId, runner.Id, path, response, stopwatch.ElapsedMilliseconds);
            if (response.Streaming)
                response = await AttachStreamAsync(response, runner, appId, cancellationToken);
            if (response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                logger.LogWarning(
                    "Runner tunnel response: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} StatusCode={StatusCode}",
                    appId,
                    deploymentTargetId,
                    runner.Id,
                    host,
                    path,
                    response.StatusCode);
            }

            return response;
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(
                exception,
                "Runtime tunnel timed out for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId,
                deploymentTargetId,
                runner.Id);
            return Failure(StatusCodes.Status504GatewayTimeout, "The Runner did not respond in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Runtime tunnel failed for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId,
                deploymentTargetId,
                runner.Id);
            return Failure(StatusCodes.Status502BadGateway, "The Runner runtime tunnel is unavailable.");
        }
    }

    // The Runner reports how long the app took to answer; the rest of the round trip is what the tunnel added.
    private void LogLatency(Guid appId, Guid targetId, Guid runnerId, string path, TunnelResponse response, long roundTripMs)
        => logger.LogInformation(
            "Tunnel latency: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} RuntimePath={RuntimePath} StatusCode={StatusCode} Streaming={Streaming} RoundTripMs={RoundTripMs} UpstreamMs={UpstreamMs} TunnelOverheadMs={TunnelOverheadMs}",
            appId, targetId, runnerId, path, response.StatusCode, response.Streaming,
            roundTripMs, response.UpstreamMs, Math.Max(0, roundTripMs - response.UpstreamMs));

    // A streaming response holds one of the app's long-lived slots until the browser reads it to the end or goes away.
    private async Task<TunnelResponse> AttachStreamAsync(
        TunnelResponse response, Runner runner, Guid appId, CancellationToken cancellationToken)
    {
        var connection = hub.Clients.Client(runner.ConnectionId!);
        var cancel = new Func<Task>(async () =>
        {
            registry.EndStream(response.RequestId, new OperationCanceledException());
            try { await connection.SendAsync(AutomatonHubMethods.TunnelCancel, response.RequestId, CancellationToken.None); }
            catch (Exception exception) { logger.LogDebug(exception, "Could not cancel tunnel stream {RequestId}", response.RequestId); }
        });

        var lease = limiter.TryAcquire(appId, config.MaxLongLivedPerApp);
        if (lease is null)
        {
            await cancel();
            logger.LogWarning("Tunnel stream rejected: AppId={AppId} RunnerId={RunnerId} too many long-lived connections", appId, runner.Id);
            return Failure(StatusCodes.Status503ServiceUnavailable, "This app has too many open streaming connections.");
        }

        var unacknowledged = 0;
        response.BodyStream = new TunnelBodyStream(
            response.BodyStream!.Reader,
            async () =>
            {
                if (++unacknowledged < AckEveryChunks) return;
                unacknowledged = 0;
                await connection.SendAsync(AutomatonHubMethods.TunnelAck, response.RequestId, cancellationToken);
            },
            cancel,
            lease);
        return response;
    }

    private static TunnelRequest CreateTunnelRequest(
        string requestId,
        Guid appId,
        Guid deploymentTargetId,
        HttpRequest request,
        string path,
        byte[]? body,
        bool staticSite)
        => new()
        {
            RequestId = requestId,
            Method = request.Method,
            BodyBytes = body,
            HeaderValues = CreateForwardedHeaders(request),
            Destination = staticSite ? TunnelDestinations.StaticSite : TunnelDestinations.RuntimeGateway,
            RuntimeAppId = appId,
            RuntimeDeploymentTargetId = deploymentTargetId,
            RuntimePath = path.TrimStart('/') + request.QueryString
        };

    internal static TunnelRequest CreateWebSocketRequest(
        string requestId, Guid appId, Guid deploymentTargetId, HttpRequest request, string path)
    {
        var headers = CreateForwardedHeaders(request);
        var subProtocols = headers.TryGetValue("Sec-WebSocket-Protocol", out var offered)
            ? offered.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray()
            : [];
        return new()
        {
            RequestId = requestId,
            Method = HttpMethods.Get,
            HeaderValues = headers,
            Destination = TunnelDestinations.RuntimeGateway,
            RuntimeAppId = appId,
            RuntimeDeploymentTargetId = deploymentTargetId,
            RuntimePath = path.TrimStart('/') + request.QueryString,
            WebSocket = true,
            WebSocketSubProtocols = subProtocols
        };
    }

    // The Runner drops Host, so the app must learn the public host and scheme from X-Forwarded-*. Forwarded headers sent
    // by the client are replaced: the API already resolved the address the request was really reached at.
    internal static Dictionary<string, string[]> CreateForwardedHeaders(HttpRequest request)
    {
        var headers = request.Headers
            .Where(header => !HopByHopHeaders.Contains(header.Key))
            .ToDictionary(
                header => header.Key,
                header => header.Value.OfType<string>().ToArray(),
                StringComparer.OrdinalIgnoreCase);
        headers["X-Forwarded-Host"] = [request.Host.ToString()];
        headers["X-Forwarded-Proto"] = [request.Scheme];
        return headers;
    }

    internal static async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0)
        {
            return (null, false);
        }

        await using var body = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var remaining = maxBytes + 1;
        while (remaining > 0)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }

        return body.Length > maxBytes
            ? (null, true)
            : (body.ToArray(), false);
    }

    private static TunnelResponse Failure(int statusCode, string error) => new()
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
