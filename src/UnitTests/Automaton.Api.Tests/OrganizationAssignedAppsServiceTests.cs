using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class OrganizationAssignedAppsServiceTests
{
    [Test]
    public async Task AssignedAppsAreSafeAndRequireActiveMembershipAndAssignment()
    {
        var evidence = await OrganizationAssignedAppsScenario.RunAsync();
        Assert.That(evidence.Count, Is.EqualTo(1));
        Assert.That(evidence.MetadataSafe, Is.True);
        Assert.That(evidence.NotProjectAccessible, Is.True);
        Assert.That(evidence.MembershipRevocationDenied, Is.True);
        Assert.That(evidence.AssignmentRevocationDenied, Is.True);
    }
}

internal sealed record AssignedAppsEvidence(
    int Count,
    bool MetadataSafe,
    bool NotProjectAccessible,
    bool MembershipRevocationDenied,
    bool AssignmentRevocationDenied);

internal static class OrganizationAssignedAppsScenario
{
    public static async Task<AssignedAppsEvidence> RunAsync()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAssignedAppAsync(db);
        var service = CreateService(db, fixture.UserId);
        var controller = CreateController(service, fixture.UserId);
        var response = await controller.GetCurrentUserAssignedApps(CancellationToken.None);
        var summaries = ((IEnumerable<OrganizationAppSummaryDto>)((OkObjectResult)response.Result!).Value!).ToArray();
        var summaryJson = JsonSerializer.Serialize(summaries.Single());
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(fixture.UserId));
        var notProjectAccessible = !await authorization.CanAccessAppAsync(fixture.AppId, fixture.UserId);

        await SetMembershipAsync(db, false);
        var membershipRevoked = (await service.GetMyAssignedAppsAsync(fixture.UserId)).Count == 0;
        await SetMembershipAsync(db, true);
        await SetAssignmentAsync(db, false);
        var assignmentRevoked = (await service.GetMyAssignedAppsAsync(fixture.UserId)).Count == 0;

        return new AssignedAppsEvidence(
            summaries.Length,
            !summaryJson.Contains("definition", StringComparison.OrdinalIgnoreCase) && !summaryJson.Contains("source secret", StringComparison.Ordinal),
            notProjectAccessible,
            membershipRevoked,
            assignmentRevoked);
    }

    private static ApplicationDbContext CreateDbContext() => AppAuthorizationTestData.CreateDbContext();

    private static async Task<AssignedAppFixture> SeedAssignedAppAsync(ApplicationDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "Test organization",
            OwnerId = Guid.NewGuid(),
            Slug = $"test-{organizationId:N}",
            CreatedAt = now,
            IsActive = true
        });
        var project = AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "test-project");
        project.OrganizationId = organizationId;
        db.Projects.Add(project);
        db.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Assigned app",
            Summary = "Safe summary",
            Description = "Safe description",
            Definition = "source secret",
            ProjectId = projectId,
            Scope = AccessScope.Project,
            Created = now,
            Updated = now
        });
        db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = appId, AddedAt = now });
        db.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = OrganizationRole.AppUser,
            JoinedAt = now,
            IsActive = true
        });
        db.OrganizationAppAssignments.Add(new OrganizationAppAssignment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppId = appId,
            UserId = userId,
            AssignedAt = now,
            IsActive = true
        });
        await db.SaveChangesAsync();
        return new AssignedAppFixture(organizationId, appId, userId);
    }

    private static OrganizationService CreateService(ApplicationDbContext db, Guid userId)
        => new(
            db,
            AppAuthorizationTestData.CreateUserContextService(userId),
            null!,
            new OrganizationAiPolicySettingsService(db),
            null!);

    private static OrganizationsController CreateController(OrganizationService service, Guid userId)
        => new(service, null!, NullLogger<OrganizationsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
                }
            }
        };

    private static async Task SetMembershipAsync(ApplicationDbContext db, bool active)
    {
        db.OrganizationMembers.Single().IsActive = active;
        await db.SaveChangesAsync();
    }

    private static async Task SetAssignmentAsync(ApplicationDbContext db, bool active)
    {
        db.OrganizationAppAssignments.Single().IsActive = active;
        await db.SaveChangesAsync();
    }

    private sealed record AssignedAppFixture(Guid OrganizationId, Guid AppId, Guid UserId);
}
