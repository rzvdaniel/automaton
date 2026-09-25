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
public class AppAuthorizationRegressionTests
{
    [Test]
    public async Task ListAsync_ProjectsAuthorizedPersonalAppAndUnassignedRunner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "personal-app");
        dbContext.WebApps.Single().Summary = "Card summary";
        await dbContext.SaveChangesAsync();

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);
        var result = await service.ListAsync(cancellationToken: CancellationToken.None);

        var app = result.Single();
        Assert.Multiple(() =>
        {
            Assert.That(app.Id, Is.EqualTo(appId));
            Assert.That(app.Summary, Is.EqualTo("Card summary"));
            Assert.That(app.Runner, Is.Null);
            Assert.That(app.CanDelete, Is.True);
            Assert.That(app.Runtime.GenericRuntimeDeployed, Is.False);
        });
    }


    [Test]
    public async Task ListAsync_ProjectsAssignedRunnerAndCanonicalPresence()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "runner-app");
        dbContext.AppDeploymentTargets.Single().ExecutionRunnerId = runnerId;
        var now = DateTime.UtcNow;
        dbContext.Runners.Add(new Runner { Id = runnerId, Name = "runner", Purpose = RunnerPurposes.Managed, ConnectionId = "connection", LastHeartbeatAtUtc = now, Status = "offline" });
        await dbContext.SaveChangesAsync();

        var app = (await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId).ListAsync(cancellationToken: CancellationToken.None)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(app.Runner!.Name, Is.EqualTo("runner"));
            Assert.That(app.Runner.Purpose, Is.EqualTo(RunnerPurposes.Managed));
            Assert.That(app.Runner.ConnectivityStatus, Is.EqualTo(RunnerStatus.Online));
        });
    }


    [Test]
    public async Task ListAsync_ProjectsProjectAppWithoutDeleteCapability()
    {
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, appId, projectId, ownerId, collaboratorId);

        var apps = await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId).ListAsync(projectId, CancellationToken.None);

        Assert.That(apps.Single().CanDelete, Is.False);
    }


    [Test]
    public async Task ListAsync_DoesNotLeakAppsFromAnotherProject()
    {
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var allowedProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, Guid.NewGuid(), allowedProjectId, ownerId, collaboratorId);
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, Guid.NewGuid(), otherProjectId, ownerId, Guid.NewGuid());

        var apps = await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId).ListAsync(allowedProjectId, CancellationToken.None);

        Assert.That(apps, Has.Count.EqualTo(1));
        Assert.That(apps.Single().Name, Is.EqualTo("Project App"));
    }


    [Test]
    public async Task ListTagSuggestionsAsync_ReturnsNormalizedPersonalTagsInPredictableOrder()
    {
        var ownerId = Guid.NewGuid();
        var firstAppId = Guid.NewGuid();
        var secondAppId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, firstAppId, ownerId, "first-app");
        AppAuthorizationTestData.SeedApp(dbContext, secondAppId, ownerId, "second-app");
        dbContext.WebApps.Single(app => app.Id == firstAppId).Tags = " Games, strategy, games ";
        dbContext.WebApps.Single(app => app.Id == secondAppId).Tags = "Automation,Strategy";
        await dbContext.SaveChangesAsync();

        var tags = await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId).ListTagSuggestionsAsync(cancellationToken: CancellationToken.None);

        Assert.That(tags, Is.EqualTo(new[] { "Automation", "Games", "Strategy" }));
    }


    [Test]
    public async Task ListTagSuggestionsAsync_DoesNotLeakTagsFromAnotherProject()
    {
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var allowedProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var allowedAppId = Guid.NewGuid();
        var otherAppId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, allowedAppId, allowedProjectId, ownerId, collaboratorId);
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, otherAppId, otherProjectId, ownerId, Guid.NewGuid());
        dbContext.WebApps.Single(app => app.Id == allowedAppId).Tags = "Shared, Team";
        dbContext.WebApps.Single(app => app.Id == otherAppId).Tags = "Confidential";
        await dbContext.SaveChangesAsync();

        var tags = await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId)
            .ListTagSuggestionsAsync(allowedProjectId, CancellationToken.None);

        Assert.That(tags, Is.EqualTo(new[] { "Shared", "Team" }));
    }


    [Test]
    public async Task ListTagSuggestionsAsync_DoesNotIncludeProjectTagsInPersonalContext()
    {
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var personalAppId = Guid.NewGuid();
        var projectAppId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, personalAppId, collaboratorId, "personal-app");
        AppAuthorizationTestData.SeedProjectScopedApp(dbContext, projectAppId, projectId, ownerId, collaboratorId);
        dbContext.WebApps.Single(app => app.Id == personalAppId).Tags = "Personal";
        dbContext.WebApps.Single(app => app.Id == projectAppId).Tags = "Project-only";
        await dbContext.SaveChangesAsync();

        var tags = await AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, collaboratorId)
            .ListTagSuggestionsAsync(cancellationToken: CancellationToken.None);

        Assert.That(tags, Is.EqualTo(new[] { "Personal" }));
    }
}
