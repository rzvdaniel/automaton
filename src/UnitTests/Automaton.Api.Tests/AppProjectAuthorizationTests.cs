using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Automaton.Api.Tests;

[TestFixture]
public class AppProjectAuthorizationTests
{

    [Test]
    public async Task GetPublishedAppInfoAsync_UsesProjectOwnerAsOwnerId()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        dbContext.Projects.Add(new Project
        {
            Id = projectId,
            Name = "project",
            DisplayName = "Project",
            Description = string.Empty,
            OwnerId = projectOwnerId,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsActive = true
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Project App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            PublicUrl = "project-app",
            IsPublished = true,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            AccessStatus = AppAccessStatuses.Active,
            ProjectId = projectId,
            Scope = AccessScope.Project,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = collaboratorId
        });
        await dbContext.SaveChangesAsync();

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId);

        var result = await service.GetPublishedAppInfoAsync("project-app", CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.OwnerId, Is.EqualTo(projectOwnerId));
    }


    [Test]
    public void Get_ReturnsProjectOwnerAsUserId_ForProjectScopedApp()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, appId, projectId, projectOwnerId, collaboratorId);

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId, tempRoot.Path);

        var result = service.Get(appId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.UserId, Is.EqualTo(projectOwnerId));
    }


    [Test]
    public async Task Get_LetsAnAdministratorOpenAnyAppReadOnly_ButNotAStranger()
    {
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, appId, Guid.NewGuid(), ownerId, collaboratorId);
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.Administrator, NormalizedName = UserRole.Administrator.ToUpperInvariant() };
        dbContext.Roles.Add(role);
        dbContext.UserRoles.Add(new ApplicationUserRole { UserId = adminId, RoleId = role.Id });
        await dbContext.SaveChangesAsync();

        var asAdmin = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, adminId, tempRoot.Path).Get(appId);
        var asStranger = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, strangerId, tempRoot.Path).Get(appId);

        Assert.Multiple(() =>
        {
            Assert.That(asAdmin, Is.Not.Null);
            Assert.That(asAdmin!.CanUpdate, Is.False);
            Assert.That(asStranger, Is.Null);
        });
    }

    [Test]
    public async Task CreateSnapshot_IsForThePeopleWhoManageTheApp_NotForAnAdministratorWhoOnlyReadsIt()
    {
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "snapshot-app");
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.Administrator, NormalizedName = UserRole.Administrator.ToUpperInvariant() };
        dbContext.Roles.Add(role);
        dbContext.UserRoles.Add(new ApplicationUserRole { UserId = adminId, RoleId = role.Id });
        await dbContext.SaveChangesAsync();

        async Task<int?> PostSnapshotAs(Guid userId)
        {
            var apps = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, userId, tempRoot.Path, out var storage);
            var controller = new AppVersionsController(new AppVersionService(dbContext, storage), apps, NullLogger<AppVersionsController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
                    }
                }
            };
            var result = await controller.CreateSnapshot(appId);
            return (result.Result as ObjectResult)?.StatusCode;
        }

        var asAdmin = await PostSnapshotAs(adminId);
        var asOwner = await PostSnapshotAs(ownerId);

        Assert.Multiple(() =>
        {
            Assert.That(asAdmin, Is.EqualTo(StatusCodes.Status403Forbidden));
            // The owner passes the access check; what follows is the snapshot itself.
            Assert.That(asOwner, Is.Not.EqualTo(StatusCodes.Status403Forbidden).And.Not.EqualTo(StatusCodes.Status404NotFound));
        });
    }

    [Test]
    public void List_ReturnsProjectOwnerAsUserId_ForProjectScopedApp()
    {
        var projectOwnerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, appId, projectId, projectOwnerId, collaboratorId);

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId, tempRoot.Path);

        var result = service.ListForMcp(projectId).Single();

        Assert.That(result.UserId, Is.EqualTo(projectOwnerId));
    }
}
