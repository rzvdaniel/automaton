using Automaton.Api.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Middleware;

/// <summary>Limits Node bootstrap and management paths to their configured local listeners.</summary>
public sealed class NodeListenerRouteIsolationMiddleware(
    RequestDelegate next,
    IOptions<NodeListenerOptions> options)
{
    private readonly NodeListenerOptions listeners = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var isolatedListener = NodeListenerRoutePolicy.IsIsolatedListener(context.Connection.LocalPort, listeners);
        var nodePath = NodeListenerRoutePolicy.IsNodePath(context.Request.Path);
        if ((isolatedListener || nodePath) &&
            !NodeListenerRoutePolicy.Allows(context.Request.Method, context.Request.Path, context.Connection.LocalPort, listeners))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }

        await next(context);
    }
}

internal static class NodeListenerRoutePolicy
{
    public static bool IsIsolatedListener(int localPort, NodeListenerOptions listeners) =>
        listeners.ManagementPort == localPort || listeners.BootstrapPort == localPort;

    public static bool IsNodePath(PathString path) =>
        path.StartsWithSegments("/api/node", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/node-enrollments", StringComparison.OrdinalIgnoreCase);

    public static bool Allows(string method, PathString path, int localPort, NodeListenerOptions listeners)
    {
        if (listeners.ManagementPort is int managementPort && localPort == managementPort)
            return HttpMethods.IsGet(method) && string.Equals(path.Value, "/api/node/v1/identity", StringComparison.Ordinal);

        if (listeners.BootstrapPort is int bootstrapPort && localPort == bootstrapPort)
            return IsBootstrapOperation(method, path);

        return !IsNodePath(path);
    }

    private static bool IsBootstrapOperation(string method, PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not { Length: >= 4 } ||
            !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], "node-enrollments", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(segments[2], "D", out _))
            return false;

        return (segments.Length == 4 && string.Equals(segments[3], "complete", StringComparison.Ordinal) &&
                HttpMethods.IsPost(method)) ||
               (segments.Length == 5 && string.Equals(segments[3], "recovery", StringComparison.Ordinal) &&
                HttpMethods.IsPost(method) &&
                (string.Equals(segments[4], "challenge", StringComparison.Ordinal) ||
                 string.Equals(segments[4], "complete", StringComparison.Ordinal)));
    }
}
