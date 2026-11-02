using Automaton.Api.Entities;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Authentication;

/// <summary>
/// Holds a user who must choose a new password (for example after an administrator created the account with a
/// password they know) to the password change. Their access tokens carry <see cref="ClaimType"/>; every other API
/// call is refused with 403 and <see cref="ErrorCode"/>, so neither Studio nor the CLI or API keys work before the
/// change. Changing the password revokes the session, and the next sign-in issues tokens without the claim.
/// </summary>
public sealed class PasswordChangeRequirement(RequestDelegate next)
{
    public const string ClaimType = "pwd_change_required";
    public const string ErrorCode = "password_change_required";

    // What the change-password page needs: the profile, settings, the change itself and signing out.
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

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(ClaimType, "true") && IsBlocked(context.Request.Path))
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

    internal static bool IsBlocked(PathString path)
        => path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !AllowedPaths.Any(allowed => allowed.EndsWith('/')
                ? path.StartsWithSegments(allowed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                : path.Equals(allowed, StringComparison.OrdinalIgnoreCase));
}
