using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Serilog;
using Yarp.ReverseProxy.Forwarder;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Serves the requests the API sends through the tunnel: picks the fixed local destination named by the tunnel headers
/// (runtime gateway, static site or FastAPI) and forwards to it with YARP, streaming both ways.
/// </summary>
internal sealed class RunnerTunnelRequestHandler
{
    private static readonly ILogger Logger = Log.ForContext<RunnerTunnelRequestHandler>();

    private readonly IHttpForwarder forwarder;
    private readonly TunnelForwarderConfig config;
    private readonly RunnerAppSlots slots;
    private readonly MachineStaticSitePublisher? staticSitePublisher;
    private readonly HttpMessageInvoker invoker;
    private readonly ForwarderRequestConfig requestConfig;

    public RunnerTunnelRequestHandler(
        IHttpForwarder forwarder,
        TunnelForwarderConfig config,
        MachineStaticSitePublisher? staticSitePublisher)
    {
        this.forwarder = forwarder;
        this.config = config;
        this.staticSitePublisher = staticSitePublisher;
        slots = new RunnerAppSlots(config);
        // Redirects belong to the browser: following them here would request the app's redirect target on the Runner's
        // loopback gateway (404) instead of returning the 301/302 and its Location to the visitor.
        invoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            InitialHttp2StreamWindowSize = config.Http2StreamWindowBytes
        });
        requestConfig = new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            AllowResponseBuffering = false
        };
    }

    /// <summary>Called before each request; false while the Runner is switching to another API instance.</summary>
    public Func<bool> TryBeginRequest { get; set; } = () => true;

    /// <summary>Called when a request that <see cref="TryBeginRequest"/> accepted has ended.</summary>
    public Action CompleteRequest { get; set; } = () => { };

    public async Task HandleAsync(HttpContext context)
    {
        if (!TryBeginRequest())
        {
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable,
                "Runner is reconnecting to the active API for runtime tunnel traffic.");
            return;
        }

        try { await ForwardAsync(context); }
        finally { CompleteRequest(); }
    }

    private async Task ForwardAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var route = await ResolveAsync(context, cancellationToken);
        if (route is null)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "Runtime tunnel destination is invalid.");
            return;
        }

        using var slot = await slots.TryEnterAsync(route.AppId, cancellationToken);
        if (slot is null)
        {
            Logger.Warning("Tunnel request refused: app {AppId} has too many requests in flight", route.AppId);
            await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, "This app is busy on the Runner. Try again shortly.");
            return;
        }

        Logger.Debug("Forwarding through {Destination}: {Method} {Uri}", route.Destination, context.Request.Method, route.Uri.AbsolutePath);
        var error = await forwarder.SendAsync(
            context, route.Uri.GetLeftPart(UriPartial.Authority) + "/", invoker, requestConfig, new RouteTransformer(route.Uri));
        if (error != ForwarderError.None && !context.Response.HasStarted)
            Logger.Warning("Tunnel forward failed: {Error} for {Method} {Path}", error, context.Request.Method, route.Uri.AbsolutePath);
    }

    private static Task RefuseAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, context.RequestAborted);
    }

    internal sealed record Route(string Destination, Uri Uri, Guid? AppId);

    internal async Task<Route?> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var headers = context.Request.Headers;
        var destination = headers[TunnelHeaders.Destination].ToString();
        var pathAndQuery = context.Request.Path.ToUriComponent() + context.Request.QueryString.Value;
        if (destination == TunnelDestinations.FastApi)
            return new Route(destination, new Uri(new Uri(config.FastApiUrl), pathAndQuery), null);

        if (destination is not (TunnelDestinations.RuntimeGateway or TunnelDestinations.StaticSite)
            || !Guid.TryParse(headers[TunnelHeaders.AppId].ToString(), out var appId)
            || !Guid.TryParse(headers[TunnelHeaders.TargetId].ToString(), out var targetId)
            || !TryReadSafeRuntimePath(pathAndQuery, out var rawPath, out var query))
            return null;

        if (destination == TunnelDestinations.RuntimeGateway)
            return new Route(destination,
                new Uri(new Uri(config.RuntimeGatewayUrl), $"/apps/{appId:D}/targets/{targetId:D}/{rawPath}{query}"), appId);

        if (staticSitePublisher is null) return null;
        var endpoint = await staticSitePublisher.GetEndpointAsync(appId, targetId, cancellationToken);
        return endpoint is null
            ? null
            : new Route(destination, new Uri(endpoint.BaseAddress, endpoint.PathPrefix + rawPath + query), appId);
    }

    /// <summary>The path arrives as the API's relative path ("/x/y?q"); it must stay inside the app: no dot segments, no backslashes, no absolute URLs.</summary>
    internal static bool TryReadSafeRuntimePath(string? pathAndQuery, out string rawPath, out string query)
    {
        rawPath = string.Empty;
        query = string.Empty;
        var relativePath = (pathAndQuery ?? string.Empty).TrimStart('/');
        if (relativePath.StartsWith('\\') || relativePath.Contains('\\')) return false;

        var queryIndex = relativePath.IndexOf('?');
        rawPath = queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
        query = queryIndex >= 0 ? relativePath[queryIndex..] : string.Empty;
        if (rawPath.Contains("://", StringComparison.Ordinal)) return false;

        foreach (var segment in rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string decodedSegment;
            try { decodedSegment = Uri.UnescapeDataString(segment); }
            catch (UriFormatException) { return false; }
            if (decodedSegment is "." or ".." || decodedSegment.Contains('\\') || decodedSegment.Contains('/')) return false;
        }
        return true;
    }

    private sealed class RouteTransformer(Uri target) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            proxyRequest.RequestUri = target;
            proxyRequest.Headers.Host = null;
            foreach (var header in TunnelHeaders.All)
                proxyRequest.Headers.Remove(header);
        }
    }
}
