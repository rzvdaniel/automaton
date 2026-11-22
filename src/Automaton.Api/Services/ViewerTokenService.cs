using Microsoft.AspNetCore.DataProtection;

namespace Automaton.Api.Services;

public static class ViewerHeaders
{
    public const string Role = "X-Automaton-Viewer-Role";
    public const string Token = "X-Automaton-Viewer-Token";
}

/// <summary>
/// A short-lived signed token the runtime auth proxy hands to an app for the person using it. It is bound to one app and
/// one viewer, so an app can pass it back to the API to show on whose behalf it changes server settings.
/// </summary>
public sealed class ViewerTokenService(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("Automaton.ViewerToken.v1").ToTimeLimitedDataProtector();

    public string Create(Guid appId, Guid userId, TimeSpan? lifetime = null)
        => protector.Protect($"{appId:N}.{userId:N}", lifetime ?? Lifetime);

    /// <summary>The viewer's user id when the token is genuine, unexpired and issued for this app; otherwise null.</summary>
    public Guid? Validate(string? token, Guid appId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024) return null;
        try
        {
            var parts = protector.Unprotect(token.Trim()).Split('.');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var tokenApp) || tokenApp != appId
                || !Guid.TryParseExact(parts[1], "N", out var user)) return null;
            return user;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
