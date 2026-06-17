using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Security;

public sealed class RefreshTokenCookieService
{
    public const string CookieName = "automaton_refresh_token";

    public string? GetRefreshToken(HttpContext httpContext)
    {
        return httpContext.Request.Cookies.TryGetValue(CookieName, out var token) ? token : null;
    }

    public void AppendRefreshToken(HttpContext httpContext, string refreshToken, int lifetimeDays)
    {
        httpContext.Response.Cookies.Append(
            CookieName,
            refreshToken,
            BuildCookieOptions(httpContext, lifetimeDays));
    }

    public void ClearRefreshToken(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(
            CookieName,
            BuildCookieOptions(httpContext, 0));
    }

    private static CookieOptions BuildCookieOptions(HttpContext httpContext, int lifetimeDays)
    {
        var host = httpContext.Request.Host.Host;
        var isLocalhost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                          host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = isLocalhost ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = lifetimeDays > 0 ? DateTimeOffset.UtcNow.AddDays(lifetimeDays) : DateTimeOffset.UtcNow.AddDays(-1),
            IsEssential = true,
            Path = "/"
        };
    }
}
