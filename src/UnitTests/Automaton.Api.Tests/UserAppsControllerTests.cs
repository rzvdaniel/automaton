using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Automaton.Api.Tests;

[TestFixture]
public class UserAppsControllerTests
{
    [Test]
    public async Task GenerateUniqueSlug_EnforcesPublishedHostContract()
    {
        await using var dbContext = CreateDbContext();
        var httpContext = new DefaultHttpContext();
        var appsService = UserAppsTestServices.CreateAppsService(dbContext, httpContext);
        var longName = new string('x', 200);

        var generated = appsService.GenerateUniqueSlug(longName);

        Assert.That(generated.Length, Is.LessThanOrEqualTo(PublishedAppHostNames.MaximumSlugLength));
        Assert.That(PublishedAppHostNames.IsValidPublishedSlug(generated), Is.True);
    }

    [Test]
    public async Task GenerateUniqueSlug_AddsBoundedSuffixWhenBaseIsTaken()
    {
        await using var dbContext = CreateDbContext();
        var httpContext = new DefaultHttpContext();
        var appsService = UserAppsTestServices.CreateAppsService(dbContext, httpContext);
        var baseName = new string('x', 63);
        dbContext.WebApps.Add(new WebApp
        {
            Id = Guid.NewGuid(),
            Name = "Existing app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            PublicUrl = baseName
        });
        await dbContext.SaveChangesAsync();

        var generated = appsService.GenerateUniqueSlug(baseName);

        Assert.That(generated.Length, Is.LessThanOrEqualTo(PublishedAppHostNames.MaximumSlugLength));
        Assert.That(generated, Is.EqualTo($"{new string('x', 61)}-1"));
    }

    [Test]
    public async Task GenerateUniqueSlug_DoesNotUseReservedPrivateHostLabel()
    {
        await using var dbContext = CreateDbContext();
        var appsService = UserAppsTestServices.CreateAppsService(dbContext, new DefaultHttpContext());

        var generated = appsService.GenerateUniqueSlug("app-162a91472be14b93bd6a607c2afe5107");

        Assert.That(PublishedAppHostNames.IsValidPublishedSlug(generated), Is.True);
        Assert.That(PublishedAppHostNames.IsPrivateAppLabel(generated), Is.False);
    }

    [Test]
    public async Task ServeRestrictedApp_ReturnsHtmlUnauthorizedPage_ForAnonymousRequest()
    {
        var appId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        var httpContext = new DefaultHttpContext();
        var appsService = UserAppsTestServices.CreateAppsService(dbContext, httpContext);

        var controller = new UserAppsController(
            dbContext,
            appsService,
            null!,
            NullLogger<UserAppsController>.Instance,
            Options.Create(new WebAppConfig()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.ServeRestrictedApp(appId, runtimeTokens: null);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var contentResult = (ContentResult)result;
        Assert.That(contentResult.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(contentResult.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(contentResult.Content, Does.Contain("Authentication Required"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

}
