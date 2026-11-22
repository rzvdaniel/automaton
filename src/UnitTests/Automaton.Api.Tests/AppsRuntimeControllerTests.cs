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

    [Test]
    public async Task GetEmbeddable_ForUserWithoutAccess_Returns403()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await AssignedRuntimeScenario.SeedAssignedAppAsync(dbContext);
        var (controller, _) = CreateController(dbContext, Guid.NewGuid(), Forbidding(dbContext));

        var result = await controller.GetEmbeddable(fixture.AppId, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task GetEmbeddable_ForUnknownApp_Returns404()
    {
        await using var dbContext = CreateDbContext();
        var (controller, _) = CreateController(dbContext, Guid.NewGuid(), Forbidding(dbContext));

        var result = await controller.GetEmbeddable(Guid.NewGuid(), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task GetEmbeddable_ForSharedMember_ReportsWhetherTheAppCanBeFramed(bool forbids, bool expected)
    {
        await using var dbContext = CreateDbContext();
        var fixture = await AssignedRuntimeScenario.SeedAssignedAppAsync(dbContext);
        var forwarder = new AppFramingProbeTests.FakeForwarder((_, _) =>
            forbids ? AppFramingProbeTests.Response(200, ("X-Frame-Options", "DENY")) : AppFramingProbeTests.Response(200));
        var (controller, _) = CreateController(dbContext, fixture.UserId, AppFramingProbeTests.Probe(dbContext, forwarder));

        var result = await controller.GetEmbeddable(fixture.AppId, CancellationToken.None);

        var ok = (OkObjectResult)result;
        Assert.That(ok.Value!.GetType().GetProperty("canBeFramed")!.GetValue(ok.Value), Is.EqualTo(expected));
    }

    private static AppFramingProbe Forbidding(ApplicationDbContext dbContext)
        => AppFramingProbeTests.Probe(dbContext, new AppFramingProbeTests.FakeForwarder((_, _) => AppFramingProbeTests.Response(200)));

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static (AppsRuntimeController, DefaultHttpContext) CreateController(
        ApplicationDbContext dbContext, Guid userId, AppFramingProbe? probe = null)
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
            probe ?? Forbidding(dbContext),
            authorization,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.test" }))
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return (controller, http);
    }
}
