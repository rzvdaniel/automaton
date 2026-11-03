using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Authentication;

/// <summary>
/// Holds a user who must choose a new password (for example after an administrator created the account with a
/// password they know) to the password change. Every other request is refused with 403 and <see cref="ErrorCode"/>,
/// so neither Studio nor the CLI or API keys work before the change. The flag is read from the database for any
/// signed-in principal, whatever the scheme (JWT, Identity cookie after GitHub sign-in, API key); access tokens also
/// carry <see cref="ClaimType"/> so Studio and the fast path know without a lookup. Changing the password clears the
/// flag and revokes the session.
/// </summary>
public sealed class PasswordChangeRequirement(RequestDelegate next)
{
    public const string ClaimType = "pwd_change_required";
    public const string ErrorCode = "password_change_required";

    // What the change-password page needs: the profile, settings, the change itself and signing out. Everything
    // else is blocked, including non-API pages such as the server-rendered admin UI.
    private static readonly string[] AllowedPaths =
    [
        "/api/Account/UpdateUserPassword",
        "/api/User/GetUserProfile",
        "/api/user-settings",
        "/api/Token/"
    ];

    public static IDictionary<string, string> ClaimsFor(ApplicationUser user)
        => user.MustChangePassword
            ? new Dictionary<string, string> { [ClaimType] = "true" }
            : new Dictionary<string, string>();

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db, IMemoryCache cache)
    {
        if (IsBlocked(context.Request.Path) && await MustChangeAsync(context.User, db, cache, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Choose a new password before continuing.",
                code = ErrorCode
            });
            return;
        }
        await next(context);
    }

    private static async Task<bool> MustChangeAsync(ClaimsPrincipal user, ApplicationDbContext db, IMemoryCache cache,
        CancellationToken token)
    {
        if (user.HasClaim(ClaimType, "true")) return true;
        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return false;
        // Only "no change needed" is cached: the flag is set when an account is created, before it can sign in,
        // and a cleared flag must take effect at once.
        var key = (nameof(PasswordChangeRequirement), userId);
        if (cache.TryGetValue(key, out _)) return false;
        var required = await db.Users.AnyAsync(item => item.Id == userId && item.MustChangePassword, token);
        if (!required) cache.Set(key, true, TimeSpan.FromMinutes(5));
        return required;
    }

    internal static bool IsBlocked(PathString path)
        => !path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            && !path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            && !AllowedPaths.Any(allowed => allowed.EndsWith('/')
                ? path.StartsWithSegments(allowed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                : path.Equals(allowed, StringComparison.OrdinalIgnoreCase));
}
