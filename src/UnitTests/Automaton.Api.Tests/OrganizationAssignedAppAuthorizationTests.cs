using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class OrganizationAssignedAppAuthorizationTests
{
    [Test]
    public async Task CanExecuteAppAsync_AllowsAssignedOrganizationMemberWithoutProjectMembership()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.True);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesUnassignedOrganizationMember()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesAssignmentAfterOrganizationMembershipIsRevoked()
    {
        await using var fixture = await AppFixture.CreateAsync(assignApp: true, memberIsActive: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesUnassignedAppUserInOrganizationProject()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: false,
            projectRole: ProjectRole.AppUser);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_DeniesProjectAppUserAfterOrganizationMembershipIsRevoked()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: true,
            memberIsActive: false,
            projectRole: ProjectRole.AppUser);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.False);
    }

    [Test]
    public async Task CanExecuteAppAsync_PreservesPersonalProjectAppUserExecution()
    {
        await using var fixture = await AppFixture.CreateAsync(
            assignApp: false,
            projectRole: ProjectRole.AppUser,
            organizationScoped: false);

        var canExecute = await fixture.Authorization.CanExecuteAppAsync(fixture.AppId, fixture.UserId);

        Assert.That(canExecute, Is.True);
    }

    private sealed class AppFixture : IAsyncDisposable
    {
        private AppFixture(
            ApplicationDbContext dbContext,
            ResourceAuthorizationService authorization,
            Guid appId,
            Guid userId)
        {
            DbContext = dbContext;
            Authorization = authorization;
            AppId = appId;
            UserId = userId;
        }

        public ApplicationDbContext DbContext { get; }
        public ResourceAuthorizationService Authorization { get; }
        public Guid AppId { get; }
        public Guid UserId { get; }

        public static async Task<AppFixture> CreateAsync(
            bool assignApp,
            bool memberIsActive = true,
            ProjectRole? projectRole = null,
            bool organizationScoped = true)
        {
            var dbContext = AppAuthorizationTestData.CreateDbContext();
            var organizationId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            dbContext.Organizations.Add(new Organization
            {
                Id = organizationId,
                Name = "Test organization",
                OwnerId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            var project = AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "project");
            project.OrganizationId = organizationScoped ? organizationId : null;
            dbContext.Projects.Add(project);
            dbContext.WebApps.Add(new WebApp
            {
                Id = appId,
                Name = "Assigned app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ProjectId = projectId,
                Scope = AccessScope.Project,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            });
            dbContext.ProjectApps.Add(new ProjectApp
            {
                ProjectId = projectId,
                WebAppId = appId,
                AddedAt = DateTime.UtcNow
            });
            dbContext.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                UserId = userId,
                Role = OrganizationRole.AppUser,
                JoinedAt = DateTime.UtcNow,
                IsActive = memberIsActive
            });

            if (assignApp)
            {
                dbContext.OrganizationAppAssignments.Add(new OrganizationAppAssignment
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    AppId = appId,
                    UserId = userId,
                    AssignedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            if (projectRole.HasValue)
            {
                dbContext.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    UserId = userId,
                    Role = projectRole.Value,
                    JoinedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await dbContext.SaveChangesAsync();
            return new AppFixture(
                dbContext,
                new ResourceAuthorizationService(dbContext, AppAuthorizationTestData.CreateUserContextService(userId)),
                appId,
                userId);
        }

        public ValueTask DisposeAsync() => DbContext.DisposeAsync();
    }
}
