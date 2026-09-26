using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerAccountAdmissionTests
{
    [Test]
    public async Task PrepareProvisioningAsync_RejectsMultipleNonDeletedRunnersAndPreservesThem()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedCoLocatedRunnersWithDifferentDiskLimits(db, userId, planId);
        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = false });

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true));

        Assert.That(exception!.Message, Does.Contain("administrator must resolve this conflict"));
        await HostedRunnerAccountAdmissionAssertions.AssertExistingRunnersRemainUnchangedAsync(db, userId);
    }

    [Test]
    public async Task PrepareProvisioningAsync_ReusesSoleDisabledRunner()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedCoLocatedRunnersWithDifferentDiskLimits(db, userId, planId);
        var runners = await db.HostedRunners.Where(x => x.UserId == userId).OrderBy(x => x.CreatedAt).ToListAsync();
        var disabledRunner = runners[0];
        disabledRunner.Status = HostedRunnerStatuses.Disabled;
        runners[1].DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = false });

        var preparation = await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true);

        Assert.Multiple(() =>
        {
            Assert.That(preparation.HostedRunner.Id, Is.EqualTo(disabledRunner.Id));
            Assert.That(preparation.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Pending));
            Assert.That(preparation.ShouldPublishProvisionRequest, Is.True);
        });
        Assert.That(await db.HostedRunners.CountAsync(x => x.UserId == userId && x.DeletedAt == null), Is.EqualTo(1));
    }

    [Test]
    public async Task PrepareProvisioningAsync_RejectsStorageOnMultipleHostsBeforeAdmission()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedCoLocatedRunnersWithDifferentDiskLimits(db, userId, planId);
        var runners = await db.HostedRunners.Where(x => x.UserId == userId).ToListAsync();
        runners[1].RunnerHostId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = false });

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true));

        Assert.That(exception!.Message, Does.Contain("multiple Runner Hosts"));
        Assert.That(await db.HostedRunnerDeployments.CountAsync(), Is.Zero);
        Assert.That(await db.HostedRunners.CountAsync(x => x.UserId == userId), Is.EqualTo(2));
    }
}

internal static class HostedRunnerAccountAdmissionAssertions
{
    public static async Task AssertExistingRunnersRemainUnchangedAsync(ApplicationDbContext db, Guid userId)
    {
        var runners = await db.HostedRunners.Where(x => x.UserId == userId).ToListAsync();

        Assert.That(runners, Has.Count.EqualTo(2));
        Assert.That(runners.Select(x => x.Status), Is.All.EqualTo("running"));
        Assert.That(await db.HostedRunnerDeployments.CountAsync(), Is.Zero);
    }
}
