using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class AppDeletionAdmissionAssertions
{
    public static async Task AssertRejectedAsync(
        AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture, ApplicationDbContext replica)
    {
        Assert.That(await replica.WebApps.AsNoTracking().Where(app => app.Id == fixture.App.Id)
            .Select(app => app.DeletionClaimId).SingleAsync(), Is.Not.Null);
        var peer = AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateService(
            replica, fixture.UserId, true, out var routeRoot, out _);
        try
        {
            AssertCreationRejected(peer, fixture.App.Id, fixture.UserId,
                fixture.App.DeploymentTargets.First(t => t.ExecutionRunnerId.HasValue).ExecutionRunnerId);
            await AssertDeploymentAdmissionRejectedAsync(fixture.App.Id, replica);
        }
        finally { if (Directory.Exists(routeRoot)) Directory.Delete(routeRoot, true); }
    }

    private static void AssertCreationRejected(AppDeploymentTargetManagementService peer,
        Guid appId, Guid userId, Guid? runnerId)
    {
        var creation = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => peer.CreateAsync(
            appId, new CreateAppDeploymentTargetRequest { Name = "Concurrent B", Slug = "concurrent-b", RunnerId = runnerId }, userId));
        Assert.That(creation!.Message, Does.Contain("being deleted"));
    }

    private static async Task AssertDeploymentAdmissionRejectedAsync(Guid appId, ApplicationDbContext replica)
    {
        var targetId = await replica.AppDeploymentTargets.Where(t => t.WebAppId == appId).Select(t => t.Id).FirstAsync();
        var deployment = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() =>
            new AppDeploymentTargetStateService(replica).MarkStartedAsync(appId, targetId));
        Assert.That(deployment!.Message, Does.Contain("being deleted"));
        Assert.That(await replica.AppDeploymentTargets.CountAsync(t => t.WebAppId == appId), Is.EqualTo(2));
        var publisher = new RecordingEventPublisher();
        var admission = new DeploymentOperationAdmission(replica, new ImmediateTransactionExecutor(), publisher);
        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => admission.SaveAsync(
            new DeploymentOperation { Id = Guid.NewGuid(), WebAppId = appId, PackagePath = "unused.zip" }, CancellationToken.None));
        Assert.That(await replica.DeploymentOperations.CountAsync(), Is.Zero);
        Assert.That(publisher.OperationId, Is.EqualTo(Guid.Empty));
    }

}
