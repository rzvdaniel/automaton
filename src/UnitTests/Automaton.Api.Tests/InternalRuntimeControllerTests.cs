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

        var result = await controller.AuthorizeRuntimeRequest(
            controller.Request.Headers[RuntimeCookieConstants.AppIdHeader].ToArray(),
            proxyAuthHeaders: null,
            cancellationToken: CancellationToken.None);

        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task AuthorizeRuntimeRequest_WithProxyAuthHeaderAndValidCookie_ReturnsNoContent()
    {
        var appId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var dbContext = CreateDbContext();
        await SeedRestrictedAppAsync(dbContext, appId, userId);
        var request = CreateAuthorizedRequest(appId, userId);
        var controller = CreateController(dbContext, request.HttpContext, request.CookieService);

        var result = await controller.AuthorizeRuntimeRequest(
            request.HttpContext.Request.Headers[RuntimeCookieConstants.AppIdHeader].ToArray(),
            request.HttpContext.Request.Headers[RuntimeCookieConstants.ProxyAuthHeader].ToArray(),
            CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedRestrictedAppAsync(
        ApplicationDbContext dbContext,
        Guid appId,
        Guid userId)
    {
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
        dbContext.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = userId });
        await dbContext.SaveChangesAsync();
    }

    internal static AuthorizedRuntimeRequest CreateAuthorizedRequest(Guid appId, Guid userId)
    {
        var cookieService = CreateRuntimeCookieService();
        var cookie = cookieService.CreateRuntimeCookie(appId, userId, DateTimeOffset.UtcNow.AddMinutes(5));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[RuntimeCookieConstants.ProxyAuthHeader] = "expected-proxy-key";
        httpContext.Request.Headers[RuntimeCookieConstants.AppIdHeader] = appId.ToString();
        httpContext.Request.Headers.Cookie = $"{RuntimeCookieConstants.GetCookieName(appId)}={cookie}";
        return new AuthorizedRuntimeRequest(httpContext, cookieService);
    }

    internal static InternalRuntimeController CreateController(
        ApplicationDbContext dbContext,
        DefaultHttpContext httpContext,
        RuntimeCookieService cookieService)
    {
        var userContextService = CreateUserContextService(Guid.NewGuid());
        var authorizationService = new ResourceAuthorizationService(dbContext, userContextService);
        return new InternalRuntimeController(
            dbContext,
            authorizationService,
            cookieService,
            NullLogger<InternalRuntimeController>.Instance,
            Options.Create(new PublishedAppsConfig { RuntimeProxyAuthKey = "expected-proxy-key" }))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    internal sealed record AuthorizedRuntimeRequest(
        DefaultHttpContext HttpContext,
        RuntimeCookieService CookieService);

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

    internal static RuntimeCookieService CreateRuntimeCookieService()
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
[TestFixture]
public sealed class AssignedOrganizationRuntimeAuthorizationTests
{
    [Test]
    public async Task AssignedMemberCanUseRuntimeAndRevocationStopsRequests()
    {
        var evidence = await AssignedRuntimeScenario.RunAsync();
        Assert.That(evidence.Allowed, Is.True);
        Assert.That(evidence.MembershipRevoked, Is.True);
        Assert.That(evidence.AssignmentRevoked, Is.True);
    }
}

internal sealed record AssignedRuntimeEvidence(bool Allowed, bool MembershipRevoked, bool AssignmentRevoked);

internal static class AssignedRuntimeScenario
{
    public static async Task<AssignedRuntimeEvidence> RunAsync()
    {
        var dbContext = CreateDbContext();
        await using (dbContext)
        {
            var fixture = await SeedAssignedAppAsync(dbContext);
            var request = CreateRequest(fixture.AppId, fixture.UserId);
            var controller = CreateController(dbContext, request);
            var allowed = await AuthorizeAsync(controller, request);
            dbContext.OrganizationMembers.Single().IsActive = false;
            await dbContext.SaveChangesAsync();
            var membershipRevoked = await AuthorizeAsync(controller, request);
            dbContext.OrganizationMembers.Single().IsActive = true;
            fixture.Assignment.IsActive = false;
            await dbContext.SaveChangesAsync();
            var assignmentRevoked = await AuthorizeAsync(controller, request);
            return new AssignedRuntimeEvidence(
                allowed is NoContentResult,
                membershipRevoked is ForbidResult,
                assignmentRevoked is ForbidResult);
        }
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<AssignedRuntimeFixture> SeedAssignedAppAsync(ApplicationDbContext dbContext)
    {
        var organizationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        dbContext.Organizations.Add(new Organization
        {
            Id = organizationId, Name = "Org", Slug = $"org-{organizationId:N}", OwnerId = Guid.NewGuid(),
            CreatedAt = now, IsActive = true
        });
        var project = AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "assigned-project");
        project.OrganizationId = organizationId;
        dbContext.Projects.Add(project);
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId, Name = "Assigned", Summary = "", Description = "", Definition = "{}",
            ProjectId = projectId, Scope = AccessScope.Project,
            AccessStatus = AppAccessStatuses.Active, Created = now, Updated = now
        });
        dbContext.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = appId, AddedAt = now });
        dbContext.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = userId,
            Role = OrganizationRole.AppUser, JoinedAt = now, IsActive = true
        });
        var assignment = new OrganizationAppAssignment
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, AppId = appId, UserId = userId,
            AssignedAt = now, IsActive = true
        };
        dbContext.OrganizationAppAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
        return new AssignedRuntimeFixture(appId, userId, assignment);
    }

    private static InternalRuntimeControllerTests.AuthorizedRuntimeRequest CreateRequest(Guid appId, Guid userId)
        => InternalRuntimeControllerTests.CreateAuthorizedRequest(appId, userId);

    private static InternalRuntimeController CreateController(
        ApplicationDbContext dbContext,
        InternalRuntimeControllerTests.AuthorizedRuntimeRequest request)
        => InternalRuntimeControllerTests.CreateController(dbContext, request.HttpContext, request.CookieService);

    private static Task<IActionResult> AuthorizeAsync(
        InternalRuntimeController controller,
        InternalRuntimeControllerTests.AuthorizedRuntimeRequest request)
        => controller.AuthorizeRuntimeRequest(
            request.HttpContext.Request.Headers[RuntimeCookieConstants.AppIdHeader].ToArray(),
            request.HttpContext.Request.Headers[RuntimeCookieConstants.ProxyAuthHeader].ToArray(),
            CancellationToken.None);

    private sealed record AssignedRuntimeFixture(Guid AppId, Guid UserId, OrganizationAppAssignment Assignment);
}
