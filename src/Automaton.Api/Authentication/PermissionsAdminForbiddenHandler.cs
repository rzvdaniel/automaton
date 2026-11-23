using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Automaton.Api.Authentication;

/// <summary>
/// Says why a signed-in caller is refused on the app-permission endpoints. Without it a normal CLI key gets a bare 403
/// and the CLI can only print "API error 403 Forbidden:". Every other endpoint keeps the default behaviour.
/// </summary>
public sealed class PermissionsAdminForbiddenHandler : IAuthorizationMiddlewareResultHandler
{
    public const string Reason = "Approving permissions needs a system administrator's permissions-admin key: run `automaton login --permissions-admin`.";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var usesPolicy = context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(data => data.Policy == PermissionsAdminKey.PolicyName) == true;
        if (authorizeResult.Forbidden && usesPolicy)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = "Forbidden", message = Reason });
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
