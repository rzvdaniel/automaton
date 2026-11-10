using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Authentication;

public sealed class AppPermissionAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "AppPermission";
    public const string BackupsReadPolicy = "AppPermission:backups:read";
    public const string ServerReadPolicy = "AppPermission:server:read";
    public const string NotifyEmailPolicy = "AppPermission:notify:email";

    /// <summary>Allows only an app whose approved credential carries the permission; administrators and API keys are refused because the call acts on behalf of an app.</summary>
    public static void AddAppOnlyPermissionPolicy(Microsoft.AspNetCore.Authorization.AuthorizationOptions options, string policyName, string permission)
        => options.AddPolicy(policyName, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasClaim("app_permission", permission)
                && Guid.TryParse(context.User.FindFirst("app_id")?.Value, out _)));

    /// <summary>Allows system administrators (signed in or with a Developer API key) and apps whose approved credential carries the permission.</summary>
    public static void AddPermissionPolicy(Microsoft.AspNetCore.Authorization.AuthorizationOptions options, string policyName, string permission)
        => options.AddPolicy(policyName, policy => policy
            .AddAuthenticationSchemes(Scheme, ApiKeyAuthenticationSchemeOptions.DefaultScheme, Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, "Identity.Application")
            .RequireAuthenticatedUser()
            .RequireAssertion(context => (context.User.IsInRole(Automaton.Api.Enums.UserRole.SystemAdministrator)
                    && context.User.FindFirst("api_key_scope")?.Value is null or nameof(Automaton.Core.Enums.ApiKeyScope.Developer))
                || context.User.HasClaim("app_permission", permission)));
}

public sealed class AppPermissionAuthenticationHandler(
    IOptionsMonitor<AppPermissionAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    Automaton.Api.Services.AppPermissionService permissions)
    : AuthenticationHandler<AppPermissionAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var credential = await permissions.ValidateCredentialAsync(authorization[7..].Trim(), Context.RequestAborted);
        if (credential is null) return AuthenticateResult.Fail("Invalid or revoked app permission key.");
        var claims = new List<Claim> { new("app_id", credential.Value.AppId.ToString()) };
        claims.AddRange(credential.Value.Permissions.Select(permission => new Claim("app_permission", permission)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
