using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Authentication;

public sealed class AppPermissionAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "AppPermission";
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
        var claims = new[]
        {
            new Claim("app_id", credential.Value.AppId.ToString()),
            new Claim("app_permission", credential.Value.Permission)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
