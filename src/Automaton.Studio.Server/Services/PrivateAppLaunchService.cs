using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Caching.Memory;
using Automaton.Api.Authentication;

namespace Automaton.Api.Services;

public static class PrivateAppAuthConstants
{
    public const string CookieName = "automaton_private_app_token";
    public const string ScopeClaim = "automaton_private_app";
    public const string AppIdClaim = "automaton_private_app_id";
}

public sealed class PrivateAppLaunchUrlResponse
{
    public required string Url { get; init; }
}

internal sealed record PrivateAppLaunchEntry(Guid AppId, Guid UserId, DateTimeOffset ExpiresAtUtc);

public class PrivateAppLaunchService
{
    private const string LaunchPrefix = "private-app-launch:";
    private static readonly TimeSpan LaunchCodeLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RuntimeTokenLifetime = TimeSpan.FromHours(8);

    private readonly IMemoryCache memoryCache;
    private readonly JwtOptions jwtOptions;
    private readonly ILogger<PrivateAppLaunchService> logger;
    private readonly IWebHostEnvironment environment;

    public PrivateAppLaunchService(
        IMemoryCache memoryCache,
        JwtOptions jwtOptions,
        ILogger<PrivateAppLaunchService> logger,
        IWebHostEnvironment environment)
    {
        this.memoryCache = memoryCache;
        this.jwtOptions = jwtOptions;
        this.logger = logger;
        this.environment = environment;
    }

    public string CreateLaunchCode(Guid appId, Guid userId)
    {
        var code = Guid.NewGuid().ToString("N");
        var cacheKey = BuildLaunchKey(code);
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(LaunchCodeLifetime);

        memoryCache.Set(
            cacheKey,
            new PrivateAppLaunchEntry(appId, userId, expiresAtUtc),
            expiresAtUtc);

        return code;
    }

    public bool TryConsumeLaunchCode(Guid appId, string? code, out Guid userId)
    {
        userId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var cacheKey = BuildLaunchKey(code.Trim());
        if (!memoryCache.TryGetValue<PrivateAppLaunchEntry>(cacheKey, out var entry) || entry is null)
        {
            return false;
        }

        memoryCache.Remove(cacheKey);

        if (entry.AppId != appId || entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            logger.LogWarning("Rejected private app launch code for app {AppId}", appId);
            return false;
        }

        userId = entry.UserId;
        return true;
    }

    public string CreateRuntimeToken(Guid appId, Guid userId)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(PrivateAppAuthConstants.ScopeClaim, "true"),
            new Claim(PrivateAppAuthConstants.AppIdClaim, appId.ToString())
        };

        var jwt = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(RuntimeTokenLifetime),
            signingCredentials: jwtOptions.SigningCredentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public bool IsScopedPrivateAppPrincipal(ClaimsPrincipal user, Guid appId)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var isPrivateAppToken = string.Equals(
            user.FindFirstValue(PrivateAppAuthConstants.ScopeClaim),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!isPrivateAppToken)
        {
            return false;
        }

        return Guid.TryParse(user.FindFirstValue(PrivateAppAuthConstants.AppIdClaim), out var tokenAppId)
            && tokenAppId == appId;
    }

    public CookieOptions BuildRuntimeCookieOptions(HttpRequest request)
    {
        var secure = !environment.IsDevelopment() || request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(RuntimeTokenLifetime)
        };
    }

    private static string BuildLaunchKey(string code) => $"{LaunchPrefix}{code}";
}
