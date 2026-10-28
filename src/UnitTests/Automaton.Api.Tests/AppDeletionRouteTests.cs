using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public sealed class AppDeletionRouteTests
{
    [Test]
    public async Task StaleEdgeRoute_CannotForwardAnAppWithAPendingDeletionClaim()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var runnerId = Guid.NewGuid();
        BackendRouteConfigTestFixture.AddMachineRunner(db, runnerId);
        var app = AddDeployedApp(db, runnerId);
        app.DeletionClaimId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var request = RuntimeRequest(app.Id);
        var service = CreateTunnel(db);

        var result = await service.ForwardAsync(app.Id, app.DeploymentTargets.Single().Id, request, "", CancellationToken.None);

        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task DeletionClaim_RemovesOnlyThatAppsTraefikRoutesAndRemovesLastRouteFile()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var runnerId = Guid.NewGuid();
        BackendRouteConfigTestFixture.AddMachineRunner(db, runnerId);
        var first = AddDeployedApp(db, runnerId);
        var second = AddDeployedApp(db, runnerId);
        await db.SaveChangesAsync();
        var routes = BackendRouteConfigTestFixture.CreateService(db, directory.Path);
        var configPath = Path.Combine(directory.Path, "managed-runtime-routes.yml");
        await routes.RefreshAsync();
        Assert.That(await File.ReadAllTextAsync(configPath), Does.Contain(first.Id.ToString("N")));

        first.DeletionClaimId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await routes.RefreshAsync();
        var config = await File.ReadAllTextAsync(configPath);
        Assert.That(config, Does.Not.Contain(first.Id.ToString("N")));
        Assert.That(config, Does.Contain(second.Id.ToString("N")));

        second.DeletionClaimId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await routes.RefreshAsync();
        Assert.That(File.Exists(configPath), Is.False);
    }

    [Test]
    public async Task FailedTraefikCleanup_PreservesAppRecordAndFilesForRetry()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var owner = Guid.NewGuid();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, owner, "tasks");
        var storage = CreateStorage(db, owner, appId, directory.Path);
        var blockedRoot = Path.Combine(directory.Path, "blocked-routes");
        await File.WriteAllTextAsync(blockedRoot, "not a directory");
        var routes = BackendRouteConfigTestFixture.CreateService(db, blockedRoot);
        var deletion = new AppDeletionService(db, storage, routeConfigService: routes);
        var app = await db.WebApps.SingleAsync(item => item.Id == appId);

        Assert.ThrowsAsync<IOException>(() => deletion.DeleteResourcesAsync(appId, app, owner));
        Assert.That(await db.WebApps.AnyAsync(item => item.Id == appId), Is.True);
        Assert.That(storage.AppExists(owner, appId), Is.True);

        File.Delete(blockedRoot);
        await deletion.DeleteResourcesAsync(appId, app, owner);
        Assert.That(await db.WebApps.AnyAsync(item => item.Id == appId), Is.False);
        Assert.That(storage.AppExists(owner, appId), Is.False);
    }

    private static AppFileStorageService CreateStorage(Automaton.Api.Data.ApplicationDbContext db, Guid owner, Guid appId, string path)
    {
        AppAuthorizationTestServiceFactory.CreateAppsService(db, owner, path, out var storage);
        var root = storage.GetAppRootPath(owner, appId);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "source.json"), "{}");
        return storage;
    }

    private static HttpRequest RuntimeRequest(Guid appId)
    {
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString(PublishedAppHostNames.BuildPrivateAppHost(appId, "apps.test"));
        return request;
    }

    private static RuntimeTunnelService CreateTunnel(Automaton.Api.Data.ApplicationDbContext db)
        => new(db, null!, Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.test" }),
            null!, NullLogger<RuntimeTunnelService>.Instance);

    private static WebApp AddDeployedApp(Automaton.Api.Data.ApplicationDbContext db, Guid runnerId)
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        BackendRouteConfigTestFixture.AddApp(db, appId, target);
        return db.WebApps.Local.Single(app => app.Id == appId);
    }
}
