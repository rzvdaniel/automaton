using Automaton.Api.Extensions;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

/// <summary>
/// Every API route needs sign-in unless it is on <see cref="PublicRoutes"/>. Controllers get sign-in from the global
/// AuthorizeFilter (ApiControllerRegistration); a route is public only through [AllowAnonymous]. A new endpoint without
/// [Authorize] or a new [AllowAnonymous] one therefore fails here until it is protected or named in the allowlist.
/// </summary>
[TestFixture]
public sealed class ApiRouteAuthorizationTests
{
    // Public on purpose. Each entry: "VERB route" and the reason. Controller-level [AllowAnonymous] covers every action.
    private static readonly string[] PublicRoutes =
    [
        // sign-in
        "POST /api/Account/LoginUser - sign-in",
        "GET /api/Account/IsPublicUsernameAvailable - sign-in",
        "POST /api/Account/ForgotUserPassword - sign-in",
        "POST /api/Account/ResetUserPassword - sign-in",
        "GET /api/Account/RegistrationStatus - sign-in",
        "POST /api/Account/RegisterUser - sign-in",
        "GET /api/Account/ConfirmEmail - sign-in",
        "POST /api/Token/RefreshAccessToken - sign-in",
        "POST /api/Token/RevokeRefreshToken - sign-in",
        "POST /api/Token/Logout - sign-in",
        "GET /auth/github/login - sign-in",
        "GET /auth/github/callback - sign-in",
        // invite and first-run install, checked by token or setup state
        "GET /api/invitations/validate/{token} - invite and first-run install",
        "POST /api/invitations/accept/{token}/complete - invite and first-run install",
        "GET /api/Install/status - invite and first-run install",
        "POST /api/Install/checks - invite and first-run install",
        "POST /api/Install - invite and first-run install",
        "POST /api/Install/password-link - invite and first-run install",
        "POST /api/Install/runner-installation - invite and first-run install",
        "GET /api/Install/runner-installation/{installationId} - invite and first-run install",
        // CLI and runner enrollment sign-in, checked by one-time code
        "POST /api/cli-auth/requests - CLI and runner enrollment sign-in",
        "POST /api/cli-auth/requests/{requestId:guid}/poll - CLI and runner enrollment sign-in",
        "POST /api/cli-auth/requests/{requestId:guid}/complete - CLI and runner enrollment sign-in",
        "POST /api/runner-enrollments/exchange - CLI and runner enrollment sign-in",
        // AI connector OAuth and discovery
        "GET /.well-known/oauth-protected-resource - AI connector OAuth and discovery",
        "GET /.well-known/oauth-protected-resource/mcp - AI connector OAuth and discovery",
        "GET /.well-known/oauth-authorization-server - AI connector OAuth and discovery",
        "POST /oauth/register - AI connector OAuth and discovery",
        "GET /oauth/authorize - AI connector OAuth and discovery",
        "POST /oauth/token - AI connector OAuth and discovery",
        // public app pages, store and marketplace catalog
        "GET /api/public/{publicUsername}/apps - public app pages",
        "GET /api/public/{publicUsername}/profile - public app pages",
        "GET /api/public/{publicUsername}/apps/{appId:guid} - public app pages",
        "GET /api/public/marketplace/apps - public app pages",
        "GET /api/public/marketplace/apps/{appId:guid} - public app pages",
        "GET /api/public/marketplace/apps/{appId:guid}/creator-apps - public app pages",
        "GET /api/public/marketplace/categories - public app pages",
        "POST /api/public/marketplace/apps/{appId:guid}/launch - public app pages",
        "GET /api/public/store/status - public app pages",
        "GET /api/Apps/samples - public app pages",
        "GET /api/Apps/templates - public app pages",
        // published apps serve visitors; the app's own access rules apply
        "GET /a/{appId:guid} - published apps serve visitors; the app's own access rules apply",
        "GET /a/{appId:guid}/{*path} - published apps serve visitors; the app's own access rules apply",
        "GET /apps/{slug}/media/{*path} - published apps serve visitors; the app's own access rules apply",
        "GET /{slug}/media/{*path} - published apps serve visitors; the app's own access rules apply",
        "POST /api/web-apps/{slug}/files/upload - published apps serve visitors; the app's own access rules apply",
        "GET /api/web-apps/{slug}/files/{sessionId} - published apps serve visitors; the app's own access rules apply",
        // checked by its own secret or key, not a sign-in
        "POST /api/hooks/{id:guid} - checked by its own secret or key",
        "POST /api/ApiKeys/validate - checked by its own secret or key",
        "GET /internal/runtime/auth - checked by its own secret or key",
        "POST /internal/runners/reconnect-runtime-tunnels - checked by its own secret or key",
        "GET,POST,PUT,PATCH,DELETE,HEAD,OPTIONS /runtime-tunnel/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path} - checked by its own secret or key",
    ];

    [Test]
    public async Task EveryRoute_RequiresSignIn_UnlessOnTheAllowlist()
    {
        using var host = await StartHostAsync();
        var violations = new List<string>();
        var publicSeen = new HashSet<string>();
        var globalFilter = host.Services.GetRequiredService<IOptions<MvcOptions>>().Value.Filters
            .OfType<AuthorizeFilter>().Any(f => f.Policy?.Requirements.Any(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement) == true);
        Assert.That(globalFilter, Is.True, "MVC must keep the global sign-in AuthorizeFilter.");

        foreach (var endpoint in host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText ?? "(no pattern)";
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var verbs = methods is { Count: > 0 } ? string.Join(",", methods) : "ANY";
            var name = $"{verbs} /{route.TrimStart('/')}";
            var isController = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is not null;
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var protectedRoute = !anonymous && (isController || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any());
            if (protectedRoute) continue;

            publicSeen.Add(name);
            if (!PublicRoutes.Any(p => p.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase)))
                violations.Add(name);
        }

        Assert.That(violations, Is.Empty,
            "These routes are public and not on the allowlist (add [Authorize] or name them in PublicRoutes with a reason):\n" + string.Join("\n", violations));
        var stale = PublicRoutes.Where(p => !publicSeen.Contains(p[..p.IndexOf(" - ", StringComparison.Ordinal)])).ToList();
        Assert.That(stale, Is.Empty, "Allowlist entries that are no longer public routes:\n" + string.Join("\n", stale));
    }

    private static async Task<IHost> StartHostAsync() =>
        await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                s.AddSignalR();
                s.AddAuthorization();
                s.AddApiControllers();
                s.AddMvcCore().AddApplicationPart(typeof(RunnerTunnelEndpoint).Assembly);
                // Only so the tunnel endpoint can bind its parameters; no request is ever sent.
                s.AddSingleton(_ => (RunnerTunnelRegistry)null!);
                s.AddSingleton(_ => (RunnerExecutionIdentityService)null!);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapControllers();
                    e.MapHub<Automaton.Api.Hubs.AutomatonHub>("/api/workflow/hub");
                    e.MapRunnerTunnel();
                });
            });
        }).StartAsync();
}
