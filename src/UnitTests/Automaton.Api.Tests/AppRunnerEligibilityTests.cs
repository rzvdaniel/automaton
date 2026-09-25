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
public class AppRunnerEligibilityTests
{

    [Test]
    public async Task Publish_WhenGenericRuntimeIsNotDeployed_AllowsRunnerAssignment()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var currentRunnerId = Guid.NewGuid();
        var nextRunnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        AppAuthorizationDeploymentTestData.SetDefaultDeploymentTarget(dbContext, appId, currentRunnerId, genericRuntimeDeployed: false);
        AppAuthorizationRunnerTestData.SeedManagedRunner(dbContext, nextRunnerId, ownerId, "replacement-runner");
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        await service.Publish(
            appId,
            "published-app",
            executionRunnerId: nextRunnerId,
            runtimeVisibility: AppRuntimeVisibilities.Private,
            catalogVisibility: AppCatalogVisibilities.Unlisted);

        var persisted = AppAuthorizationDeploymentTestData.ReadDefaultDeploymentTarget(dbContext, appId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(nextRunnerId));
            Assert.That(persisted.GenericRuntimeDeployed, Is.False);
        });
    }


    [Test]
    public async Task UpdateRunner_RejectsInfrastructureRunner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        AppAuthorizationRunnerTestData.SeedInfrastructureRunner(dbContext, runnerId, ownerId);
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.UpdateRunner(appId, runnerId));

        Assert.That(exception!.Message, Does.Contain("Infrastructure Runners"));
        Assert.That(AppAuthorizationDeploymentTestData.ReadDefaultDeploymentTarget(dbContext, appId).ExecutionRunnerId, Is.Null);
    }


    [TestCase("infrastructure")]
    public async Task UpdateRunner_TagDoesNotGrantInfrastructureRunnerAssignment(string tag)
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        AppAuthorizationRunnerTestData.SeedInfrastructureRunner(dbContext, runnerId, ownerId, tag);
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.UpdateRunner(appId, runnerId));

        Assert.That(exception!.Message, Does.Contain("Infrastructure Runners"));
        Assert.That(AppAuthorizationDeploymentTestData.ReadDefaultDeploymentTarget(dbContext, appId).ExecutionRunnerId, Is.Null);
    }


    [TestCase("system")]
    [TestCase("admin")]
    public async Task UpdateRunner_PrivilegedLookingTagsDoNotExpandRunnerEligibility(string tag)
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        AppAuthorizationRunnerTestData.SeedWorkflowOnlyRunner(dbContext, runnerId, ownerId, tag);
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.UpdateRunner(appId, runnerId));

        Assert.That(exception!.Message, Does.Contain("not eligible"));
        Assert.That(AppAuthorizationDeploymentTestData.ReadDefaultDeploymentTarget(dbContext, appId).ExecutionRunnerId, Is.Null);
    }


    [Test]
    public async Task Create_RejectsRunnerOutsideCallerScope()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationRunnerTestData.SeedUnassignedManagedRunner(dbContext, runnerId, otherUserId);
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.Create(new NewApp
            {
                Name = "Rejected App",
                ExecutionRunnerId = runnerId
            }));

        Assert.That(exception!.Message, Does.Contain("access"));
        Assert.That(await dbContext.WebApps.CountAsync(), Is.Zero);
    }
}
