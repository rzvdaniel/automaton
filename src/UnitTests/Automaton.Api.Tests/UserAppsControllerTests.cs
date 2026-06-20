using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class UserAppsControllerTests
{
    [Test]
    public async Task ServeStudioHostedAppMedia_ReturnsUnauthorized_ForProtectedPublishedApp_WhenAnonymous()
    {
        var appId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string slug = "protected-app";

        await using var dbContext = CreateDbContext();
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Protected App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            PublicUrl = slug,
            IsPublished = true,
            RequiresAuthentication = true,
            AccessStatus = AppAccessStatuses.Active,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });
        await dbContext.SaveChangesAsync();

        var controller = new UserAppsController(
            dbContext,
            null!,
            null!,
            null!,
            null!,
            NullLogger<UserAppsController>.Instance,
            new StubWebHostEnvironment(),
            Options.Create(new WebAppConfig()),
            null!)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.ServeStudioHostedAppMedia(slug, "uploads/test.jpg", CancellationToken.None);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.UnauthorizedResult>());
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
