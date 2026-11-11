using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>Decides from an app's own response headers whether browsers will let Studio show it in a frame.</summary>
public static class AppFramingPolicy
{
    /// <summary>
    /// True when the app's headers stop <paramref name="studioOrigin"/> from framing it. A CSP frame-ancestors directive
    /// decides alone when present (browsers then ignore X-Frame-Options); otherwise DENY or SAMEORIGIN forbid framing.
    /// ALLOW-FROM is obsolete and ignored by browsers, so it does not forbid.
    /// </summary>
    public static bool ForbidsFraming(IReadOnlyDictionary<string, string[]> headers, string studioOrigin)
    {
        var withFrameAncestors = headers
            .Where(header => header.Key.Equals("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .SelectMany(value => value.Split(','))
            .Where(HasFrameAncestors)
            .ToList();
        if (withFrameAncestors.Count > 0)
            return withFrameAncestors.Any(policy => FrameAncestorsBlock(policy, studioOrigin));

        return headers
            .Where(header => header.Key.Equals("X-Frame-Options", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries))
            .Any(token => token.Equals("DENY", StringComparison.OrdinalIgnoreCase)
                || token.Equals("SAMEORIGIN", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFrameAncestors(string policy)
        => policy.Split(';', StringSplitOptions.TrimEntries)
            .Any(directive => directive.StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase));

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

/// <summary>Sends a request for an app through its Runner tunnel.</summary>
public interface ITunnelForwarder
{
    Task<TunnelResponse> ForwardAsync(Guid appId, Guid deploymentTargetId, HttpRequest request, string path, CancellationToken cancellationToken);
}

/// <summary>Asks an app's root page, through the Runner tunnel, whether it can be shown inside Studio.</summary>
public sealed class AppFramingProbe(
    ApplicationDbContext dbContext,
    ITunnelForwarder tunnelService,
    IOptions<PublishedAppsConfig> publishedAppsOptions,
    ILogger<AppFramingProbe> logger)
{
    private const int MaxRedirects = 3;

    /// <summary>One budget for the whole check, all hops included.</summary>
    internal TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>False only when the app answered and forbids framing; any failure or timeout keeps today's framed view.</summary>
    public async Task<bool> CanBeFramedAsync(Guid appId, string studioOrigin, CancellationToken callerToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(Budget);
        var cancellationToken = budget.Token;
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
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            logger.LogWarning("Checking whether app {AppId} can be framed ran out of time", appId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not check whether app {AppId} can be framed", appId);
        }

        return true;
    }
}
