using Automaton.Api.Config;
using Automaton.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Decides from an app's own response headers whether browsers will let Studio show it in a frame.</summary>
public static class AppFramingPolicy
{
    /// <summary>True when X-Frame-Options or a CSP frame-ancestors rule stops <paramref name="studioOrigin"/> from framing the app.</summary>
    public static bool ForbidsFraming(IReadOnlyDictionary<string, string[]> headers, string studioOrigin)
    {
        foreach (var (name, values) in headers)
        {
            if (name.Equals("X-Frame-Options", StringComparison.OrdinalIgnoreCase)
                && values.Any(value => value.Trim().ToUpperInvariant() is "DENY" or "SAMEORIGIN"))
                return true;

            if (name.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase)
                && values.SelectMany(value => value.Split(',')).Any(policy => FrameAncestorsBlock(policy, studioOrigin)))
                return true;
        }

        return false;
    }

    private static bool FrameAncestorsBlock(string policy, string studioOrigin)
    {
        foreach (var directive in policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = directive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("frame-ancestors", StringComparison.OrdinalIgnoreCase)) continue;
            return !parts.Skip(1).Any(source => AllowsOrigin(source, studioOrigin));
        }

        return false;
    }

    private static bool AllowsOrigin(string source, string studioOrigin)
    {
        if (source == "*") return true;
        if (source.Equals(studioOrigin, StringComparison.OrdinalIgnoreCase)) return true;
        // Host-only and wildcard-host sources such as studio.example.com or *.example.com.
        if (!Uri.TryCreate(studioOrigin, UriKind.Absolute, out var origin)) return false;
        var hostSource = source.Contains("://") ? source[(source.IndexOf("://", StringComparison.Ordinal) + 3)..] : source;
        var host = hostSource.TrimEnd('/').Split(':')[0];
        if (host.StartsWith("*.", StringComparison.Ordinal))
            return origin.Host.EndsWith(host[1..], StringComparison.OrdinalIgnoreCase);
        return host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Asks an app's root page, through the Runner tunnel, whether it can be shown inside Studio.</summary>
public sealed class AppFramingProbe(
    ApplicationDbContext dbContext,
    RuntimeTunnelService tunnelService,
    IOptions<PublishedAppsConfig> publishedAppsOptions,
    ILogger<AppFramingProbe> logger)
{
    private const int MaxRedirects = 3;

    /// <summary>False only when the app answered and forbids framing; any failure keeps today's framed view.</summary>
    public async Task<bool> CanBeFramedAsync(Guid appId, string studioOrigin, CancellationToken cancellationToken)
    {
        try
        {
            var targetId = await dbContext.AppDeploymentTargets
                .Where(target => target.WebAppId == appId && target.IsDefault)
                .Select(target => target.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (targetId == Guid.Empty) return true;

            var host = PublishedAppHostNames.BuildPrivateAppHost(appId, publishedAppsOptions.Value.PublicBaseUrlHost);
            var path = string.Empty;
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                var context = new DefaultHttpContext();
                context.Request.Method = HttpMethods.Get;
                context.Request.Scheme = "https";
                context.Request.Host = new HostString(host);

                var response = await tunnelService.ForwardAsync(appId, targetId, context.Request, path, cancellationToken);
                if (response.BodyStream is { } stream)
                {
                    await stream.CancelAsync();
                    stream.Dispose();
                }

                if (response.StatusCode is >= 300 and < 400
                    && response.HeaderValues.FirstOrDefault(h => h.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } location
                    && location[0].StartsWith('/'))
                {
                    path = location[0].TrimStart('/');
                    continue;
                }

                return !AppFramingPolicy.ForbidsFraming(response.HeaderValues, studioOrigin);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not check whether app {AppId} can be framed", appId);
        }

        return true;
    }
}
