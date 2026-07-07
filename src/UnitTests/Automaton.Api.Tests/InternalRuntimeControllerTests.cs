using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class InternalRuntimeControllerTests
{
    [Test]
    public async Task AuthorizeRuntimeRequest_WithoutProxyAuthHeader_ReturnsUnauthorized()
    {
        await using var dbContext = CreateDbContext();
        var userContextService = CreateUserContextService(Guid.NewGuid());
        var controller = new InternalRuntimeController(
            dbContext,
            new ResourceAuthorizationService(dbContext, userContextService),
            CreateRuntimeCookieService(),
            NullLogger<InternalRuntimeController>.Instance,
            Options.Create(new PublishedAppsConfig
            {
                RuntimeProxyAuthKey = "expected-proxy-key"
            }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.Request.Headers[RuntimeCookieConstants.AppIdHeader] = Guid.NewGuid().ToString();

        var result = await controller.AuthorizeRuntimeRequest(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task AuthorizeRuntimeRequest_WithProxyAuthHeaderAndValidCookie_ReturnsNoContent()
    {
        var appId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Restricted App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            PublicUrl = "restricted-app",
            AccessStatus = AppAccessStatuses.Active,
            RequiresAuthentication = true,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = userId
        });
        await dbContext.SaveChangesAsync();

        var runtimeCookieService = CreateRuntimeCookieService();
        var cookie = runtimeCookieService.CreateRuntimeCookie(appId, userId, DateTimeOffset.UtcNow.AddMinutes(5));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[RuntimeCookieConstants.ProxyAuthHeader] = "expected-proxy-key";
        httpContext.Request.Headers[RuntimeCookieConstants.AppIdHeader] = appId.ToString();
        httpContext.Request.Headers.Cookie = $"{RuntimeCookieConstants.CookieName}={cookie}";

        var userContextService = CreateUserContextService(Guid.NewGuid());
        var controller = new InternalRuntimeController(
            dbContext,
            new ResourceAuthorizationService(dbContext, userContextService),
            runtimeCookieService,
            NullLogger<InternalRuntimeController>.Instance,
            Options.Create(new PublishedAppsConfig
            {
                RuntimeProxyAuthKey = "expected-proxy-key"
            }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.AuthorizeRuntimeRequest(CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static UserContextService CreateUserContextService(Guid userId)
    {
        return new UserContextService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Test"))
            }
        });
    }

    private static RuntimeCookieService CreateRuntimeCookieService()
    {
        var provider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(
            Directory.CreateTempSubdirectory("automaton-runtime-internal-auth-"));

        return new RuntimeCookieService(
            provider,
            Options.Create(new PublishedAppsConfig
            {
                PublicBaseUrl = "https://apps.automaton.monster",
                ApiBaseUrl = "https://api.automaton.monster"
            }));
    }
}
