using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Automaton.Api.Services;

/// <summary>
/// Sends a browser request to an app through the Runner's tunnel connection as a real HTTP/2 stream. YARP does the
/// forwarding (streaming bodies in both directions, cancellation, WebSocket upgrades), so nothing is buffered here.
/// </summary>
public sealed class RuntimeTunnelRequestSender(
    IHttpForwarder forwarder,
    RunnerTunnelRegistry tunnels,
    TunnelLongLivedLimiter limiter,
    IOptions<RuntimeTunnelConfig> options,
    ILogger<RuntimeTunnelRequestSender> logger)
{
    // The tunnel has no real host; the Runner picks its local destination from the TunnelHeaders.
    internal const string TunnelBaseAddress = "http://runner.tunnel/";

    private readonly RuntimeTunnelConfig config = options.Value;

    public async Task ProxyAsync(
        HttpContext context,
        Guid appId,
        Guid deploymentTargetId,
        Runner runner,
        string path,
        bool staticSite)
    {
        var cancellationToken = context.RequestAborted;
        var isWebSocket = context.WebSockets.IsWebSocketRequest;
        if (isWebSocket && staticSite)
        {
            await TunnelFailure.Text(StatusCodes.Status400BadRequest, "Static sites do not use WebSockets.")
                .WriteAsync(context.Response, cancellationToken);
            return;
        }

        using var slot = isWebSocket ? limiter.TryAcquire(appId, config.MaxLongLivedPerApp) : NoSlot.Instance;
        if (slot is null)
        {
            await TunnelFailure.Text(StatusCodes.Status503ServiceUnavailable, "This app has too many open streaming connections.")
                .WriteAsync(context.Response, cancellationToken);
            return;
        }

        if (tunnels.TryAcquire(runner.Id) is not { } connection)
        {
            await TunnelFailure.Text(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is not connected.")
                .WriteAsync(context.Response, cancellationToken);
            return;
        }

        using var lease = connection;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var error = await forwarder.SendAsync(
            context,
            TunnelBaseAddress,
            lease.Invoker,
            new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(config.StreamIdleTimeoutSeconds),
                Version = System.Net.HttpVersion.Version20,
                VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
                AllowResponseBuffering = false
            },
            new TunnelRequestTransformer(appId, deploymentTargetId, path, staticSite));

        var status = context.Response.StatusCode;
        if (error != ForwarderError.None || status >= StatusCodes.Status400BadRequest)
        {
            logger.LogWarning(
                "Runner tunnel response: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} StatusCode={StatusCode} ForwarderError={ForwarderError} ElapsedMs={ElapsedMs}",
                appId, deploymentTargetId, runner.Id, context.Request.Host.Host, path, status, error, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>A request for the Runner: the app's path and query, plus the headers that pick the Runner's local destination.</summary>
    internal static HttpRequestMessage CreateRequest(
        HttpMethod method, Guid appId, Guid deploymentTargetId, string path, string query, HttpRequest source, bool staticSite)
    {
        var message = new HttpRequestMessage(method, BuildUri(path, query))
        {
            Version = System.Net.HttpVersion.Version20,
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact
        };
        ApplyTunnelHeaders(message.Headers, appId, deploymentTargetId, staticSite, source.Host.ToString(), source.Scheme);
        return message;
    }

    internal static Uri BuildUri(string path, string query)
        => new(TunnelBaseAddress.TrimEnd('/') + new PathString("/" + path.TrimStart('/')).ToUriComponent() + query);

    // The Runner drops Host, so the app must learn the public host and scheme from X-Forwarded-*. Forwarded headers sent
    // by the client are replaced: the API already resolved the address the request was really reached at. The tunnel
    // headers are set here and never taken from the browser.
    internal static void ApplyTunnelHeaders(
        System.Net.Http.Headers.HttpRequestHeaders headers, Guid appId, Guid deploymentTargetId, bool staticSite, string host, string scheme)
    {
        foreach (var name in TunnelHeaders.All.Concat(["X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-For", "Forwarded"]))
            headers.Remove(name);
        headers.TryAddWithoutValidation(TunnelHeaders.Destination, staticSite ? TunnelDestinations.StaticSite : TunnelDestinations.RuntimeGateway);
        headers.TryAddWithoutValidation(TunnelHeaders.AppId, appId.ToString("D"));
        headers.TryAddWithoutValidation(TunnelHeaders.TargetId, deploymentTargetId.ToString("D"));
        headers.TryAddWithoutValidation("X-Forwarded-Host", host);
        headers.TryAddWithoutValidation("X-Forwarded-Proto", scheme);
    }

    private sealed class TunnelRequestTransformer(Guid appId, Guid deploymentTargetId, string path, bool staticSite) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            proxyRequest.RequestUri = BuildUri(path, httpContext.Request.QueryString.Value ?? string.Empty);
            proxyRequest.Headers.Host = null;
            ApplyTunnelHeaders(proxyRequest.Headers, appId, deploymentTargetId, staticSite,
                httpContext.Request.Host.ToString(), httpContext.Request.Scheme);
        }
    }

    private sealed class NoSlot : IDisposable
    {
        public static readonly NoSlot Instance = new();
        public void Dispose() { }
    }
}
