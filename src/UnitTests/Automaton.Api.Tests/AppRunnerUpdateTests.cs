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
public class AppRunnerUpdateTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task Update_AuthenticationChangeRefreshesDeployedRoutes(bool requiresAuthentication)
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, ownerId, tempRoot.Path);
        await SeedDeployedAuthenticationAppAsync(db, service, ownerId, appId, runnerId, !requiresAuthentication);
        var model = service.Get(appId) ?? throw new InvalidOperationException("The seeded application should be available.");
        model.RequiresAuthentication = requiresAuthentication;

        await service.Update(appId, model);

        var content = await File.ReadAllTextAsync(Path.Combine(tempRoot.Path, "AppData", "traefik-dynamic", "managed-runtime-routes.yml"));
        Assert.That(content, Does.Contain($"private-{appId:N}-runtime"));
        Assert.That(content.Contains("forwardAuth:", StringComparison.Ordinal), Is.EqualTo(requiresAuthentication));
        Assert.That(content, Does.Contain("service: \"api-live-edge\""));
    }

    private static async Task SeedDeployedAuthenticationAppAsync(
        ApplicationDbContext db, AppsService service, Guid ownerId, Guid appId, Guid runnerId,
        bool requiresAuthentication)
    {
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "owner-app");
        AppAuthorizationRunnerTestData.SeedManagedRunner(db, runnerId, ownerId);
        _ = service.Get(appId);
        AppAuthorizationDeploymentTestData.SetDefaultDeploymentTarget(db, appId, runnerId, genericRuntimeDeployed: true);
        await SeedAuthenticationRouteAsync(db, ownerId, requiresAuthentication);
    }

    private static async Task SeedAuthenticationRouteAsync(
        ApplicationDbContext db, Guid ownerId, bool requiresAuthentication)
    {
        var entity = await db.WebApps.SingleAsync();
        entity.RequiresAuthentication = requiresAuthentication;
        entity.ApplicationContractJson = Automaton.Core.Models.ApplicationContractSerialization.Serialize(new()
        {
            Runtime = new()
            {
                Command = new() { Executable = "custom-server" }, Port = 5000
            }
        });
        await db.SaveChangesAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Update_FailedAuthenticationRouteWrite_SameValueRetryRepairsRoute(bool requiresAuthentication)
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, ownerId, tempRoot.Path);
        await SeedDeployedAuthenticationAppAsync(db, service, ownerId, appId, runnerId, requiresAuthentication);
        var model = service.Get(appId) ?? throw new InvalidOperationException("The seeded application should be available.");
        model.RequiresAuthentication = !requiresAuthentication;
        await service.Update(appId, model);
        var routePath = Path.Combine(tempRoot.Path, "AppData", "traefik-dynamic", "managed-runtime-routes.yml");
        var oldRoute = await File.ReadAllTextAsync(routePath);

        // A directory at the atomic writer's temporary path fails before replacement.
        Directory.CreateDirectory(routePath + ".tmp");
        model.RequiresAuthentication = requiresAuthentication;
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Update(appId, model));
        db.ChangeTracker.Clear();
        Assert.That((await db.WebApps.SingleAsync()).RequiresAuthentication, Is.EqualTo(requiresAuthentication));
        Assert.That(await File.ReadAllTextAsync(routePath), Is.EqualTo(oldRoute));
        Directory.Delete(routePath + ".tmp");

        // Recreate the service and model to represent a later request, not a local flag.
        var retryService = AppAuthorizationTestServiceFactory.CreateAppsService(db, ownerId, tempRoot.Path);
        var retryModel = retryService.Get(appId)
            ?? throw new InvalidOperationException("The application should remain available for the retry.");
        await retryService.Update(appId, retryModel);

        var repairedRoute = await File.ReadAllTextAsync(routePath);
        Assert.That(repairedRoute.Contains("forwardAuth:", StringComparison.Ordinal), Is.EqualTo(requiresAuthentication));
        Assert.That(repairedRoute, Is.Not.EqualTo(oldRoute));
    }

    [Test]
    public async Task Update_CannotBypassRunnerAssignmentPolicy()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);
        var model = service.Get(appId)!;
        model.ExecutionRunnerId = Guid.NewGuid();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.Update(appId, model));

        Assert.That(exception!.Message, Does.Contain("not found"));
        Assert.That(
            (await dbContext.AppDeploymentTargets.SingleAsync(item => item.WebAppId == appId && item.IsDefault)).ExecutionRunnerId,
            Is.Null);
    }


    [Test]
    public async Task Update_WhenApplicationContractIsOmitted_PreservesPersistedContract()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var contract = new Automaton.Core.Models.ApplicationContract
        {
            Runtime = new Automaton.Core.Models.ApplicationRuntimeSpec
            {
                Command = new Automaton.Core.Models.ApplicationCommandSpec
                {
                    Executable = "custom-server",
                    Arguments = ["--port", "5000"]
                },
                Port = 5000
            }
        };

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        var entity = await dbContext.WebApps.SingleAsync(app => app.Id == appId);
        entity.ApplicationContractJson = Automaton.Core.Models.ApplicationContractSerialization.Serialize(contract);
        await dbContext.SaveChangesAsync();

        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);
        var model = service.Get(appId)!;
        model.Name = "Renamed app";
        model.Application = null;

        await service.Update(appId, model);

        var persisted = Automaton.Core.Models.ApplicationContractSerialization.Deserialize(
            (await dbContext.WebApps.SingleAsync(app => app.Id == appId)).ApplicationContractJson);
        Assert.That(persisted.Runtime.Command!.Executable, Is.EqualTo("custom-server"));
        Assert.That(persisted.Runtime.Port, Is.EqualTo(5000));
    }


    [Test]
    public async Task UpdateRunner_AssignsEligibleOwnedRunner()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "hosted-runner",
            Purpose = RunnerPurposes.Managed,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed),
            Status = "offline"
        });
        dbContext.RunnerUsers.Add(new RunnerUser { RunnerId = runnerId, UserId = ownerId });
        await dbContext.SaveChangesAsync();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);

        await service.UpdateRunner(appId, runnerId);

        Assert.That(
            (await dbContext.AppDeploymentTargets.SingleAsync(item => item.WebAppId == appId && item.IsDefault)).ExecutionRunnerId,
            Is.EqualTo(runnerId));
    }


    [Test]
    public async Task UpdateRunner_WhenGenericRuntimeIsNotDeployed_AllowsRunnerReassignment()
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

        await service.UpdateRunner(appId, nextRunnerId);

        var persisted = AppAuthorizationDeploymentTestData.ReadDefaultDeploymentTarget(dbContext, appId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(nextRunnerId));
            Assert.That(persisted.GenericRuntimeDeployed, Is.False);
        });
    }


    [Test]
    public async Task Update_RejectsChangingRunnerWhenGenericRuntimeIsDeployed()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var currentRunnerId = Guid.NewGuid();
        var nextRunnerId = Guid.NewGuid();

        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var dbContext = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(dbContext, appId, ownerId, "owner-app");
        var target = dbContext.AppDeploymentTargets.Single(item => item.WebAppId == appId && item.IsDefault);
        target.ExecutionRunnerId = currentRunnerId;
        target.GenericRuntimeDeployed = true;
        await dbContext.SaveChangesAsync();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(dbContext, ownerId, tempRoot.Path);
        var model = service.Get(appId)!;
        model.ExecutionRunnerId = nextRunnerId;

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await service.Update(appId, model));

        Assert.That(exception!.Message, Does.Contain("explicitly clean up"));
        Assert.That(
            (await dbContext.AppDeploymentTargets.SingleAsync(item => item.WebAppId == appId && item.IsDefault)).ExecutionRunnerId,
            Is.EqualTo(currentRunnerId));
    }
}
