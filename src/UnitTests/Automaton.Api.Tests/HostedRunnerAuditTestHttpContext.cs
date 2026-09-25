using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

internal static class HostedRunnerAuditTestHttpContext
{
    public static IHttpContextAccessor Create(Guid userId, string userName)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, userName)
                ], "Test"))
        };
        return new HttpContextAccessor { HttpContext = httpContext };
    }
}
