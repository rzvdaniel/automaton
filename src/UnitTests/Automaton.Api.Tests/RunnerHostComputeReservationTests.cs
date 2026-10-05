using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerHostComputeReservationTests
{
    [TestCase(1, 6, 384, 768, 6, 768)]
    [TestCase(6, 1, 768, 384, 6, 768)]
    [TestCase(6, 1, 384, 768, 6, 768)]
    [TestCase(1, 6, 768, 384, 6, 768)]
    public async Task HostTotalsReserveEachComputeResourceSeparately(
        int currentCpu, int pendingCpu, int currentMemory, int pendingMemory, int expectedCpu, int expectedMemory)
    {
        await using var db = HostedRunnerAuditTestData.CreateDbContext();
        var host = HostedRunnerAuditTestData.SeedRunnerHost(db);
        var runner = Runner(host, HostedRunnerStatuses.Provisioning, currentCpu, currentMemory);
        runner.PendingSpecificationSnapshot = Snapshot(pendingCpu, pendingMemory);
        db.HostedRunners.Add(runner);
        await db.SaveChangesAsync();

        var response = await GetHostAsync(db);

        Assert.That(response.UsedCpuCores, Is.EqualTo(expectedCpu));
        Assert.That(response.UsedMemoryMb, Is.EqualTo(expectedMemory));
        Assert.That(response.HostedRunnerCount, Is.EqualTo(1));
    }

    [TestCase(HostedRunnerStatuses.Pending, false, false, true)]
    [TestCase(HostedRunnerStatuses.Provisioning, false, false, true)]
    [TestCase(HostedRunnerStatuses.Reprovisioning, true, false, true)]
    [TestCase(HostedRunnerStatuses.Running, true, false, true)]
    [TestCase(HostedRunnerStatuses.Failed, true, false, true)]
    [TestCase(HostedRunnerStatuses.Failed, false, false, false)]
    [TestCase(HostedRunnerStatuses.Deleting, true, false, false)]
    [TestCase(HostedRunnerStatuses.Disabled, true, false, false)]
    [TestCase(HostedRunnerStatuses.Deleted, true, true, false)]
    [TestCase(HostedRunnerStatuses.Running, true, true, false)]
    public async Task HostTotalsUseAdmissionOccupancyRules(string status, bool container, bool deleted, bool included)
    {
        await using var db = HostedRunnerAuditTestData.CreateDbContext();
        var host = HostedRunnerAuditTestData.SeedRunnerHost(db);
        var runner = Runner(host, status, 1, 384);
        runner.ContainerId = container ? "retained-container" : null;
        runner.DeletedAt = deleted ? DateTime.UtcNow : null;
        runner.PendingSpecificationSnapshot = Snapshot(6, 768);
        db.HostedRunners.Add(runner);
        await db.SaveChangesAsync();

        var response = await GetHostAsync(db);

        Assert.That(response.UsedCpuCores, Is.EqualTo(included ? 6 : 0));
        Assert.That(response.UsedMemoryMb, Is.EqualTo(included ? 768 : 0));
        Assert.That(response.HostedRunnerCount, Is.EqualTo(included ? 1 : 0));
    }

    [Test]
    public async Task HostTotalsReleaseDowngradeOnlyAfterSnapshotConfirmation()
    {
        await using var db = HostedRunnerAuditTestData.CreateDbContext();
        var host = HostedRunnerAuditTestData.SeedRunnerHost(db);
        var runner = Runner(host, HostedRunnerStatuses.Reprovisioning, 6, 768);
        runner.PendingSpecificationSnapshot = Snapshot(1, 384);
        db.HostedRunners.Add(runner);
        await db.SaveChangesAsync();
        var pending = await GetHostAsync(db);
        Assert.That(pending.UsedCpuCores, Is.EqualTo(6));
        Assert.That(pending.UsedMemoryMb, Is.EqualTo(768));

        runner.SpecificationSnapshot = runner.PendingSpecificationSnapshot;
        runner.PendingSpecificationSnapshot = null;
        runner.Status = HostedRunnerStatuses.Running;
        await db.SaveChangesAsync();
        var confirmed = await GetHostAsync(db);
        Assert.That(confirmed.UsedCpuCores, Is.EqualTo(1));
        Assert.That(confirmed.UsedMemoryMb, Is.EqualTo(384));
    }

    private static HostedRunner Runner(RunnerHost host, string status, int cpu, int memory) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerHostId = host.Id,
        Status = status, SpecificationSnapshot = Snapshot(cpu, memory)
    };

    private static HostedRunnerSpecificationSnapshot Snapshot(int cpu, int memory) => new()
    {
        Resources = new HostedRunnerResources { CpuLimit = cpu, MemoryLimitMb = memory }
    };

    private static async Task<RunnerHostResponse> GetHostAsync(ApplicationDbContext db)
    {
        var controller = new AdminInfrastructureRunnerHostsController(db, null!, null!, null!, null!);
        var result = await controller.GetHosts(CancellationToken.None);
        return ((IEnumerable<RunnerHostResponse>)((OkObjectResult)result.Result!).Value!).Single();
    }
}
