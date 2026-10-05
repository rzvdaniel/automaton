using Automaton.Api.Config;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class HostedRunnerMySqlCapacityDenialTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task DeniedResizeReturnsConflictAndLeavesNoPartialReservation(bool cpu)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTOMATON_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Set AUTOMATON_TEST_MYSQL_CONNECTION_STRING to a dedicated migrated test database.");
        await using var db = CreateContext(connectionString);
        var fixture = HostedRunnerMySqlSeedData.SeedComputeFixture(db, cpu, resize: true);
        if (!cpu)
        {
            fixture.Host.TotalMemoryMb = 1280;
            await db.SaveChangesAsync();
        }
        await AdmitCompetingPlacementAsync(db, fixture.AccountIds[1]);
        var enrollmentCount = await db.RunnerEnrollments.CountAsync();

        var response = await DenyResizeAsync(db, fixture.AccountIds[0]);

        Assert.That(response.Result, Is.InstanceOf<ConflictObjectResult>());
        await AssertRetainedReservationAsync(db, fixture.AccountIds[0], fixture.Host.Id, enrollmentCount);
    }

    private static async Task AdmitCompetingPlacementAsync(ApplicationDbContext db, Guid accountId)
    {
        var competing = HostedRunnerProvisioningTestServiceFactory.Create(db, accountId,
            new HostedRunnerOptions { Enabled = false });
        var preparation = await competing.PrepareProvisioningAsync(accountId, Guid.Empty);
        Assert.That(preparation.ShouldPublishProvisionRequest, Is.True);
    }

    private static ApplicationDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))).Options);

    private static async Task<ActionResult<HostedRunnerResponse>> DenyResizeAsync(ApplicationDbContext db, Guid accountId)
    {
        var runner = await db.HostedRunners.SingleAsync(x => x.UserId == accountId);
        var controller = new AdminHostedRunnersController(db, null!,
            HostedRunnerManagementTestServiceFactory.Create(db, accountId), null!);
        return await controller.ReprovisionHostedRunner(runner.Id, CancellationToken.None);
    }

    private static async Task AssertRetainedReservationAsync(ApplicationDbContext db, Guid accountId, Guid hostId, int enrollmentCount)
    {
        db.ChangeTracker.Clear();
        var retained = await db.HostedRunners.SingleAsync(x => x.UserId == accountId);
        Assert.That(retained.SpecificationSnapshot.Resources.CpuLimit, Is.EqualTo(0.25m));
        Assert.That(retained.SpecificationSnapshot.Resources.MemoryLimitMb, Is.EqualTo(384));
        Assert.That(retained.PendingSpecificationSnapshot, Is.Null);
        Assert.That(retained.ContainerId, Is.EqualTo("retained-test-container"));
        Assert.That(retained.RunnerHostId, Is.EqualTo(hostId));
        Assert.That(await db.RunnerEnrollments.CountAsync(), Is.EqualTo(enrollmentCount));
    }
}
