using System.Security.Cryptography;
using System.Text.Json;
using Automaton.Api.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public static class RuntimeCookieConstants
{
    public const string CookieName = "automaton_runtime";
    public const string AccessScope = "restricted-runtime";
    public const string AppIdHeader = "X-App-Id";
}

public sealed class RuntimeCookieValidationResult
{
    private RuntimeCookieValidationResult(bool isValid, Guid userId, Guid appId, DateTimeOffset expiresAtUtc)
    {
        IsValid = isValid;
        UserId = userId;
        AppId = appId;
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsValid { get; }
    public Guid UserId { get; }
    public Guid AppId { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public static RuntimeCookieValidationResult Valid(Guid userId, Guid appId, DateTimeOffset expiresAtUtc) =>
        new(true, userId, appId, expiresAtUtc);

    public static RuntimeCookieValidationResult Invalid() =>
        new(false, Guid.Empty, Guid.Empty, DateTimeOffset.MinValue);
}

internal sealed record RuntimeCookiePayload(
    Guid UserId,
    Guid AppId,
    string AccessScope,
    DateTimeOffset ExpiresAtUtc,
    string TokenVersion);

public class RuntimeCookieService
{
    private static readonly TimeSpan RuntimeCookieLifetime = TimeSpan.FromMinutes(10);
    private const string CurrentTokenVersion = "1";

    private readonly IDataProtector dataProtector;
    private readonly PublishedAppsConfig publishedAppsConfig;

    public RuntimeCookieService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PublishedAppsConfig> publishedAppsConfig)
    {
        dataProtector = dataProtectionProvider.CreateProtector("Automaton.RuntimeCookie.v1");
        this.publishedAppsConfig = publishedAppsConfig.Value;
    }

    public DateTimeOffset GetDefaultExpiry() => DateTimeOffset.UtcNow.Add(RuntimeCookieLifetime);

    public string CreateRuntimeCookie(Guid appId, Guid userId, DateTimeOffset? expiry = null)
    {
        var expiresAtUtc = expiry ?? DateTimeOffset.UtcNow.Add(RuntimeCookieLifetime);
        var payload = new RuntimeCookiePayload(
            userId,
            appId,
            RuntimeCookieConstants.AccessScope,
            expiresAtUtc,
            CurrentTokenVersion);

        return dataProtector.Protect(JsonSerializer.Serialize(payload));
    }

    public RuntimeCookieValidationResult ValidateRuntimeCookie(string? cookie, Guid appId)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return RuntimeCookieValidationResult.Invalid();
        }

        try
        {
            var json = dataProtector.Unprotect(cookie);
            var payload = JsonSerializer.Deserialize<RuntimeCookiePayload>(json);
            if (payload is null ||
                payload.AppId != appId ||
                payload.UserId == Guid.Empty ||
                payload.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                !string.Equals(payload.AccessScope, RuntimeCookieConstants.AccessScope, StringComparison.Ordinal) ||
                !string.Equals(payload.TokenVersion, CurrentTokenVersion, StringComparison.Ordinal))
            {
                return RuntimeCookieValidationResult.Invalid();
            }

            return RuntimeCookieValidationResult.Valid(payload.UserId, payload.AppId, payload.ExpiresAtUtc);
        }
        catch (CryptographicException)
        {
            return RuntimeCookieValidationResult.Invalid();
        }
        catch (JsonException)
        {
            return RuntimeCookieValidationResult.Invalid();
        }
    }

    public void ClearRuntimeCookie(HttpResponse response, Guid appId, HttpRequest request)
    {
        response.Cookies.Delete(RuntimeCookieConstants.CookieName, BuildRuntimeCookieOptions(appId, request, DateTimeOffset.UtcNow));
    }

    public CookieOptions BuildRuntimeCookieOptions(Guid appId, HttpRequest request, DateTimeOffset expiresAtUtc)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = $"/a/{appId}",
            MaxAge = GetCookieMaxAge(expiresAtUtc),
            Expires = expiresAtUtc
        };

        var cookieDomain = TryInferCookieDomain(publishedAppsConfig.ApiBaseUrl, publishedAppsConfig.PublicBaseUrl);
        if (cookieDomain is not null)
        {
            options.Domain = cookieDomain;
        }

        return options;
    }

    private static TimeSpan GetCookieMaxAge(DateTimeOffset expiresAtUtc)
    {
        var maxAge = expiresAtUtc - DateTimeOffset.UtcNow;
        return maxAge > TimeSpan.Zero ? maxAge : TimeSpan.Zero;
    }

    private static string? TryInferCookieDomain(string? apiBaseUrl, string? runtimeBaseUrl)
    {
        if (!TryGetHost(apiBaseUrl, out var apiHost) ||
            !TryGetHost(runtimeBaseUrl, out var runtimeHost) ||
            IsLocalhost(apiHost) ||
            IsLocalhost(runtimeHost) ||
            string.Equals(apiHost, runtimeHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var apiParts = apiHost.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var runtimeParts = runtimeHost.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (apiParts.Length < 3 ||
            runtimeParts.Length < 3 ||
            apiParts.Length != runtimeParts.Length)
        {
            return null;
        }

        var apiParent = string.Join('.', apiParts.Skip(1));
        var runtimeParent = string.Join('.', runtimeParts.Skip(1));
        if (!string.Equals(apiParent, runtimeParent, StringComparison.OrdinalIgnoreCase) ||
            !LooksLikeCookieDomainParent(apiParent))
        {
            return null;
        }

        return $".{apiParent}";
    }

    private static bool TryGetHost(string? baseUrl, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        host = uri.Host;
        return true;
    }

    private static bool LooksLikeCookieDomainParent(string domain)
    {
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => part.Length >= 2);
    }

    private static bool IsLocalhost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1", StringComparison.OrdinalIgnoreCase);
}
