using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class AppDeletionRouteFailureTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task FailedCleanupRestoresRoutesOnlyWhenFreshDeletionIntentIsReleased(bool online, bool recover)
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync();
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        await AssignRuntimeAsync(fixture, online);
        var routes = BackendRouteConfigTestFixture.CreateService(fixture.Db, directory.Path);
        await routes.RefreshAsync();
        var config = Path.Combine(directory.Path, "managed-runtime-routes.yml");
        Assert.That(File.Exists(config), Is.True);
        if (recover)
        {
            fixture.App.DeletionClaimId = Guid.NewGuid();
            await fixture.Db.SaveChangesAsync();
        }
        var deletion = new AppDeletionService(fixture.Db, null!, fixture.Service, routes);

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => deletion.DeleteResourcesAsync(
            fixture.App.Id, fixture.App, fixture.UserId, recoverInterrupted: recover));

        Assert.That(File.Exists(config), Is.EqualTo(!recover));
        if (!recover) Assert.That(await File.ReadAllTextAsync(config), Does.Contain(fixture.App.Id.ToString("N")));
        Assert.That(await HasPendingClaimAsync(fixture), Is.EqualTo(recover));
    }

    private static async Task AssignRuntimeAsync(
        AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture, bool online)
    {
        var runner = fixture.AddRunner(online);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync();
        target.ExecutionRunnerId = runner.Id;
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();
    }

    private static Task<bool> HasPendingClaimAsync(AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture)
        => fixture.Db.WebApps.AsNoTracking().AnyAsync(app => app.Id == fixture.App.Id && app.DeletionClaimId != null);
}
