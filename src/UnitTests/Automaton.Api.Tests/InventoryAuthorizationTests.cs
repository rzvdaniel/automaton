using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Enums;
using Automaton.Api.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InventoryAuthorizationTests
{
    [TestCase("/api/apps", null, null, HttpStatusCode.Unauthorized)]
    [TestCase("/api/apps", "Runner", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("/api/apps", "Developer", UserRole.User, HttpStatusCode.OK)]
    [TestCase("/api/install/summary", "Developer", UserRole.User, HttpStatusCode.Forbidden)]
    [TestCase("/api/install/summary", "Developer", UserRole.Administrator, HttpStatusCode.Forbidden)]
    [TestCase("/api/install/summary", "ReadOnly", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("/api/install/summary", "Developer", UserRole.SystemAdministrator, HttpStatusCode.OK)]
    [TestCase("/api/install/summary", "AppPermission", null, HttpStatusCode.Unauthorized)]
    [TestCase("/api/install/summaries", "Developer", UserRole.SystemAdministrator, HttpStatusCode.NotFound)]
    public async Task InventoryUsesTheDeclaredEndpointAuthorization(
        string path, string? scope, string? role, HttpStatusCode expected)
    {
        await using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();
        if (scope is not null) client.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [TestCase("GET", "/api/install/update", null, null, HttpStatusCode.Unauthorized)]
    [TestCase("POST", "/api/install/update", null, null, HttpStatusCode.Unauthorized)]
    [TestCase("GET", "/api/install/update", "Developer", UserRole.User, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "Developer", UserRole.User, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "Developer", UserRole.Administrator, HttpStatusCode.Forbidden)]
    [TestCase("GET", "/api/install/update", "Developer", UserRole.Administrator, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "ReadOnly", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "Runner", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "WebApp", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("GET", "/api/install/update", "WebApp", UserRole.SystemAdministrator, HttpStatusCode.Forbidden)]
    [TestCase("POST", "/api/install/update", "Developer", UserRole.SystemAdministrator, HttpStatusCode.OK)]
    [TestCase("GET", "/api/install/update", "Developer", UserRole.SystemAdministrator, HttpStatusCode.OK)]
    public async Task ServerUpdateIsLimitedToSystemAdministratorsWithDeveloperAccess(
        string method, string path, string? scope, string? role, HttpStatusCode expected)
    {
        await using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (scope is not null) request.Headers.Add("X-Test-Scope", scope);
        if (role is not null) request.Headers.Add("X-Test-Role", role);

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [TestCase("POST", "/api/runners/00000000-0000-0000-0000-000000000002/removal")]
    [TestCase("DELETE", "/api/runners/00000000-0000-0000-0000-000000000002")]
    public async Task RunnerManagementRejectsNonDeveloperApiKeys(string method, string path)
    {
        await using var host = BuildHost();
        await host.StartAsync();
        using var client = host.GetTestClient();
        foreach (var scope in new[] { "ReadOnly", "Runner", "WebApp" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Add("X-Test-Scope", scope);
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{scope} {method} {path}");
        }
    }

    private static WebApplication BuildHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(InstallController).Assembly);
        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, InventoryAuthenticationHandler>(ApiKeyAuthenticationSchemeOptions.DefaultScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, InventoryAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, InventoryAuthenticationHandler>("Identity.Application", _ => { });

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        // Isolate endpoint authorization from controller dependencies and database reads.
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint() is not null)
                await context.Response.WriteAsync("authorized");
            else
                await next(context);
        });
        app.MapControllers();
        return app;
    }

    private sealed class InventoryAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var scope = Request.Headers["X-Test-Scope"].ToString();
            if (Scheme.Name != ApiKeyAuthenticationSchemeOptions.DefaultScheme || scope is "" or "AppPermission")
                return Task.FromResult(AuthenticateResult.NoResult());

            Claim[] claims = [new("api_key_scope", scope), new(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString())];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
