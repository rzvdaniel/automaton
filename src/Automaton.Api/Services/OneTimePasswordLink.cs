using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Entities;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Services;

/// <summary>
/// A short-lived "set your password" link for an account whose password only the server knows, created on the
/// server by someone with root (see InstallController). It is an Identity password reset token, so it works once
/// (using it changes the security stamp) and creating a new link voids earlier ones. Identity tokens last 24 hours,
/// so the expiry and a hash of the token are kept beside the user and the reset refuses the token after
/// <see cref="Lifetime"/>.
/// </summary>
public static class OneTimePasswordLink
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const string Provider = "Automaton";
    private const string Name = "PasswordLink";

    public static async Task<(string Url, DateTimeOffset ExpiresAt)> CreateAsync(UserManager<ApplicationUser> userManager,
        ApplicationUser user, string websiteUrl, DateTimeOffset now)
    {
        // Voids any link created before, including one that is still inside its lifetime.
        var stamped = await userManager.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded) throw new InvalidOperationException("The password link could not be created.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var expiresAt = now + Lifetime;
        var stored = await userManager.SetAuthenticationTokenAsync(user, Provider, Name, $"{expiresAt.UtcTicks}:{Hash(token)}");
        if (!stored.Succeeded) throw new InvalidOperationException("The password link could not be created.");
        return (AdminUserProvisioningService.BuildPasswordSetupUrl(user, token, websiteUrl), expiresAt);
    }

    /// <summary>True when this token was issued as a one-time link and its lifetime is over.</summary>
    public static async Task<bool> IsExpiredAsync(UserManager<ApplicationUser> userManager, ApplicationUser user,
        string token, DateTimeOffset now)
    {
        var parts = (await userManager.GetAuthenticationTokenAsync(user, Provider, Name))?.Split(':');
        if (parts is not { Length: 2 } || !long.TryParse(parts[0], out var ticks)) return false;
        return string.Equals(parts[1], Hash(token), StringComparison.Ordinal)
            && now > new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static Task ClearAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
        => userManager.RemoveAuthenticationTokenAsync(user, Provider, Name);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record PasswordLinkRequest(string? Email);

public sealed record PasswordLinkResponse(string Url, DateTimeOffset ExpiresAt);
