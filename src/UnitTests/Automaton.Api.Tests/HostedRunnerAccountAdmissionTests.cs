using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerAccountAdmissionTests
{
    [Test]
    public async Task PrepareProvisioningAsync_RejectsMultipleActiveRunnersAndPreservesThem()
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
