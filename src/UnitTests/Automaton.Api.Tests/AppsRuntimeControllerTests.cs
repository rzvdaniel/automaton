using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppsRuntimeControllerTests
{
    [Test]
    public async Task CreateRuntimeLaunch_ForSharedMember_SetsCookieAndReturnsPrivateAppUrl()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await AssignedRuntimeScenario.SeedAssignedAppAsync(dbContext);
        var (controller, http) = CreateController(dbContext, fixture.UserId);

        var result = await controller.CreateRuntimeLaunch(fixture.AppId, CancellationToken.None);

        var ok = (OkObjectResult)result;
        var url = ok.Value!.GetType().GetProperty("runtimeUrl")!.GetValue(ok.Value) as string;
        Assert.That(url, Is.EqualTo($"https://app-{fixture.AppId:N}.apps.automaton.test/"));
        Assert.That(http.Response.Headers.SetCookie.ToString(), Does.Contain(RuntimeCookieConstants.GetCookieName(fixture.AppId)));
    }

    [Test]
    public async Task CreateRuntimeLaunch_ForSignedInUserWithoutShare_Returns403AndNoCookie()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await AssignedRuntimeScenario.SeedAssignedAppAsync(dbContext);
        var (controller, http) = CreateController(dbContext, Guid.NewGuid());

        var result = await controller.CreateRuntimeLaunch(fixture.AppId, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(http.Response.Headers.SetCookie.ToString(), Is.Empty);
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static (AppsRuntimeController, DefaultHttpContext) CreateController(ApplicationDbContext dbContext, Guid userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
        };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.automaton.test");
        var userContext = new UserContextService(new HttpContextAccessor { HttpContext = http });
        var authorization = new ResourceAuthorizationService(dbContext, userContext);
        var controller = new AppsRuntimeController(
            UserAppsTestServices.CreateAppsService(dbContext, http),
            userContext,
            InternalRuntimeControllerTests.CreateRuntimeCookieService(),
            authorization,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.test" }))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return (controller, http);
    }
}
