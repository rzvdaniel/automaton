using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
namespace Automaton.Api.Tests;
[TestFixture]
public sealed class AppDeletionCleanupTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task AppDeletion_CleansAllAssignedTargetsBeforeStorageAndReleasesClaimsOnFailure(bool failStorage)
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        await ConfigureTargetsAsync(fixture);
        fixture.Commands.FailStorageCleanup = failStorage;
        await DeleteAppAsync(fixture, failStorage);
        Assert.That(fixture.Commands.Requests.Select(r => r.DeleteApplicationStorage), Is.EqualTo(new[] { false, false, true }));
        Assert.That(await fixture.Db.WebApps.AnyAsync(a => a.Id == fixture.App.Id), Is.EqualTo(failStorage));
        if (failStorage)
        {
            var targets = await fixture.Db.AppDeploymentTargets.AsNoTracking().Where(t => t.WebAppId == fixture.App.Id).ToListAsync();
            Assert.That(targets.All(t => t.Status != DeploymentTargetStatus.Deploying), Is.True);
        }
    }

    [Test]
    public async Task WholeAppCleanup_OfflineAssignedRunnerPreventsStorageDeletionEvenWithoutRuntime()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        var runner = fixture.AddRunner(online: false);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(t => t.WebAppId == fixture.App.Id);
        target.ExecutionRunnerId = runner.Id;
        await fixture.Db.SaveChangesAsync();
        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => fixture.Service.ClaimAndCleanupAppRuntimesAsync(fixture.App.Id));
        Assert.That(fixture.Commands.Requests, Is.Empty);
        Assert.That(await fixture.Db.WebApps.AnyAsync(a => a.Id == fixture.App.Id), Is.True);
    }

    private static async Task DeleteAppAsync(
        AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture, bool failStorage)
    {
        var filesRoot = Path.Combine(Path.GetTempPath(), "app-deletion-test-" + Guid.NewGuid());
        AppAuthorizationTestServiceFactory.CreateAppsService(fixture.Db, fixture.UserId, filesRoot, out var storage);
        var deletion = new AppDeletionService(fixture.Db, storage, fixture.Service);
        try
        {
            if (failStorage)
                Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => deletion.DeleteResourcesAsync(fixture.App.Id, fixture.App, fixture.UserId));
            else await deletion.DeleteResourcesAsync(fixture.App.Id, fixture.App, fixture.UserId);
        }
        finally { if (Directory.Exists(filesRoot)) Directory.Delete(filesRoot, true); }
    }

    private static async Task ConfigureTargetsAsync(AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture)
    {
        var runner = fixture.AddRunner(online: true);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(t => t.WebAppId == fixture.App.Id);
        target.ExecutionRunnerId = runner.Id;
        var sibling = new AppDeploymentTarget { Id = Guid.NewGuid(), WebAppId = fixture.App.Id,
            Name = "Second", Slug = "second", ExecutionRunnerId = runner.Id };
        fixture.Db.AppDeploymentTargets.Add(sibling);
        await fixture.Db.SaveChangesAsync();
    }
}
