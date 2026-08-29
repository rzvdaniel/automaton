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
        var appsService = CreateAppsService(dbContext, httpContext);
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
        var appsService = CreateAppsService(dbContext, httpContext);
        var baseName = new string('x', 63);
        dbContext.WebApps.Add(new WebApp { Id = Guid.NewGuid(), PublicUrl = baseName });
        await dbContext.SaveChangesAsync();

        var generated = appsService.GenerateUniqueSlug(baseName);

        Assert.That(generated.Length, Is.LessThanOrEqualTo(PublishedAppHostNames.MaximumSlugLength));
        Assert.That(generated, Is.EqualTo($"{new string('x', 61)}-1"));
    }

    [Test]
    public async Task GenerateUniqueSlug_DoesNotUseReservedPrivateHostLabel()
    {
        await using var dbContext = CreateDbContext();
        var appsService = CreateAppsService(dbContext, new DefaultHttpContext());

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
        var appsService = CreateAppsService(dbContext, httpContext);

        var controller = new UserAppsController(
            dbContext,
            appsService,
            null!,
            null!,
            null!,
            NullLogger<UserAppsController>.Instance,
            new StubWebHostEnvironment(),
            Options.Create(new WebAppConfig()),
            CreatePrivateAppLaunchService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.ServeRestrictedApp(appId);

        Assert.That(result, Is.InstanceOf<ContentResult>());
        var contentResult = (ContentResult)result;
        Assert.That(contentResult.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(contentResult.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(contentResult.Content, Does.Contain("Authentication Required"));
    }

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

        var httpContext = new DefaultHttpContext();
        var appsService = CreateAppsService(dbContext, httpContext);

        var controller = new UserAppsController(
            dbContext,
            appsService,
            null!,
            null!,
            null!,
            NullLogger<UserAppsController>.Instance,
            new StubWebHostEnvironment(),
            Options.Create(new WebAppConfig()),
            CreatePrivateAppLaunchService())
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
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

    private static AppsService CreateAppsService(ApplicationDbContext dbContext, HttpContext httpContext)
    {
        var userContextService = new UserContextService(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var authorizationService = new ResourceAuthorizationService(dbContext, userContextService);
        var runnerAuthorizationPolicy = new AppRunnerAuthorizationPolicy(dbContext, authorizationService);
        var runnerAssignmentPolicy = new AppRunnerAssignmentPolicy(
            dbContext,
            new HostedRunnerAppCapacityService(dbContext),
            runnerAuthorizationPolicy,
            new AppDeploymentTargetResolver(dbContext, authorizationService, runnerAuthorizationPolicy));

        return new AppsService(
            dbContext,
            userContextService,
            null!,
            authorizationService,
            null!,
            new StubWebHostEnvironment(),
            new NoOpAuditEventService(),
            new NoOpApplicationEventService(),
            null!,
            runnerAssignmentPolicy);
    }

    private static PrivateAppLaunchService CreatePrivateAppLaunchService()
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));
        var jwtOptions = new JwtOptions
        {
            Issuer = "Automaton.Tests",
            Audience = "Automaton.Tests",
            Subject = "Automaton.Tests",
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        return new PrivateAppLaunchService(
            new MemoryCache(new MemoryCacheOptions()),
            jwtOptions,
            NullLogger<PrivateAppLaunchService>.Instance,
            new StubWebHostEnvironment());
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

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(
            AuditEventWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpApplicationEventService : IApplicationEventService
    {
        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
            Guid appId,
            ApplicationEventQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ApplicationEventDto>
            {
                Items = [],
                TotalCount = 0,
                Page = query.Page,
                PageSize = query.PageSize
            });
    }
}
