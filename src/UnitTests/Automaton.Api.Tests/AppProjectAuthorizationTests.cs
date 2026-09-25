using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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


    [Test]
    public async Task GetLinkedStepsAsync_ReturnsOnlyStepsLinkedToApp()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var linkedStepId = Guid.NewGuid();
        var unrelatedStepId = Guid.NewGuid();

        await using var dbContext = AppAuthorizationTestData.CreateDbContext();

        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Test App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = appId,
            UserId = ownerId
        });

        dbContext.CustomSteps.AddRange(
            AppAuthorizationTestData.CreateStep(linkedStepId, "linked-step"),
            AppAuthorizationTestData.CreateStep(unrelatedStepId, "unrelated-step"));

        dbContext.CustomStepUsers.AddRange(
            new CustomStepUser { CustomStepId = linkedStepId, UserId = ownerId },
            new CustomStepUser { CustomStepId = unrelatedStepId, UserId = ownerId });

        dbContext.WebAppSteps.Add(new WebAppStep
        {
            WebAppId = appId,
            CustomStepId = linkedStepId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = ownerId
        });

        await dbContext.SaveChangesAsync();

        var service = AppAuthorizationTestServiceFactory.CreateAppsStepExecutionService(dbContext);

        var steps = (await service.GetLinkedStepsAsync(appId)).ToList();

        Assert.That(steps, Has.Count.EqualTo(1));
        Assert.That(steps[0].Id, Is.EqualTo(linkedStepId));
        Assert.That(steps[0].Name, Is.EqualTo("linked-step"));
    }
}
