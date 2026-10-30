using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

internal static class AppDeploymentTargetResolverTestServices
{
    public static AppDeploymentTargetResolver CreateResolver(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator = false)
        => new(
            db,
            CreateResourceAuthorizationService(db, userId, isAdministrator),
            CreateRunnerAuthorizationPolicy(db, userId, isAdministrator));

    public static AppRunnerAuthorizationPolicy CreateRunnerAuthorizationPolicy(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator = false)
        => new(db, CreateResourceAuthorizationService(db, userId, isAdministrator));

    private static ResourceAuthorizationService CreateResourceAuthorizationService(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, UserRole.Administrator));
        }

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        return new ResourceAuthorizationService(db, new UserContextService(accessor));
    }
}
