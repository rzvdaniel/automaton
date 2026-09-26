using Automaton.Api.Controllers.Admin;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerAdminReprovisionRejectionScenario
{
    internal static async Task VerifyAsync(bool conflictingHistory, string expectedMessage)
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var scenario = await SeedAsync(db, conflictingHistory);
        var exception = await ReprovisionAsync(db, scenario);
        await AssertRunnerStateAsync(db, scenario, exception, expectedMessage);
    }

    private static async Task<Scenario> SeedAsync(ApplicationDbContext db, bool conflictingHistory)
    {
        var accountId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedCoLocatedRunnersWithDifferentDiskLimits(db, accountId, planId);
        var runners = await db.HostedRunners.Where(x => x.UserId == accountId).OrderBy(x => x.CreatedAt).ToListAsync();
        var target = runners[0];
        target.Runner!.ConnectionId = "active-connection";
        AddConflictingHostHistory(db, accountId, target, runners, conflictingHistory);
        await db.SaveChangesAsync();
        return new Scenario(accountId, target.Id, target.RunnerId);
    }

    private static void AddConflictingHostHistory(
        ApplicationDbContext db,
        Guid accountId,
        HostedRunner target,
        IReadOnlyList<HostedRunner> runners,
        bool conflictingHistory)
    {
        if (!conflictingHistory) return;
        var otherHost = HostedRunnerAuditTestData.SeedRunnerHost(db);
        runners[1].RunnerHostId = otherHost.Id;
        db.AccountStorageAllocations.Add(new AccountStorageAllocation
        {
            AccountId = accountId,
            StorageProjectId = target.StorageProjectId!.Value
        });
    }

    private static async Task<InvalidOperationException> ReprovisionAsync(ApplicationDbContext db, Scenario scenario)
    {
        var httpContext = HostedRunnerAuditTestHttpContext.Create(scenario.AccountId, "admin@automaton.test");
        var userContext = new UserContextService(httpContext);
        var provisioning = HostedRunnerProvisioningTestServiceFactory.Create(
            db, scenario.AccountId, new HostedRunnerOptions { Enabled = true });
        IAuditEventService audit = new AuditEventService(db, userContext, httpContext);
        var management = new HostedRunnerManagementService(
            db, provisioning, new HostedRunnerRecordingEventPublisher(), audit, userContext,
            NullLogger<HostedRunnerManagementService>.Instance);
        var controller = new AdminHostedRunnersController(db, provisioning, management, userContext);
        return Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.ReprovisionHostedRunner(scenario.HostedRunnerId, CancellationToken.None))!;
    }

    private static async Task AssertRunnerStateAsync(
        ApplicationDbContext db,
        Scenario scenario,
        InvalidOperationException exception,
        string expectedMessage)
    {
        db.ChangeTracker.Clear();
        var runner = await db.HostedRunners.Include(x => x.Runner).SingleAsync(x => x.Id == scenario.HostedRunnerId);
        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Does.Contain(expectedMessage));
            Assert.That(runner.RunnerId, Is.EqualTo(scenario.RunnerId));
            Assert.That(runner.Runner!.ConnectionId, Is.EqualTo("active-connection"));
            Assert.That(runner.Status, Is.EqualTo(HostedRunnerStatuses.Running));
            Assert.That(runner.PendingSpecificationSnapshot, Is.Null);
            Assert.That(db.HostedRunnerDeployments.Count(), Is.Zero);
        });
    }

    private sealed record Scenario(Guid AccountId, Guid HostedRunnerId, Guid? RunnerId);
}
