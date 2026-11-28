using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

/// <summary>
/// Fills in the security headers a response lacks (#1884), whatever the route: the API, an app's page through the
/// Runner tunnel, a static file. Headers the response already carries (an app's own CSP or framing rules) stay.
/// </summary>
public static class ResponseSecurityHeaders
{
    private const string DataCsp = "default-src 'none'; frame-ancestors 'none'";

    public static void Apply(HttpResponse response, IReadOnlyCollection<string> frameAncestorOrigins)
    {
        var headers = response.Headers;
        if (!headers.ContainsKey("X-Content-Type-Options")) headers.XContentTypeOptions = "nosniff";
        if (headers.ContainsKey("Content-Security-Policy") || headers.ContainsKey("X-Frame-Options")) return;

        var isHtml = response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
        if (!isHtml)
        {
            headers.ContentSecurityPolicy = DataCsp;
            headers.XFrameOptions = "DENY";
            return;
        }

        // An app's page: only Studio (the configured origins) and the app itself may frame it.
        var ancestors = new List<string> { "'self'" };
        ancestors.AddRange(frameAncestorOrigins.Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && origin != "*"));
        headers.ContentSecurityPolicy = "frame-ancestors " + string.Join(' ', ancestors);
    }
}
