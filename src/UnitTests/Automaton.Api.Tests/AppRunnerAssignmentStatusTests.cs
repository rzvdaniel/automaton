using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppRunnerAssignmentStatusTests
{
    [TestCase(DeploymentTargetStatus.Running)]
    [TestCase(DeploymentTargetStatus.Failed)]
    public async Task AssigningTheRunnerTheTargetAlreadyUses_KeepsItsStatus(DeploymentTargetStatus status)
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var userId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = "r", Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed),
            Status = "Online"
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = userId });
        var app = new WebApp { Id = Guid.NewGuid(), Name = "Harbour" };
        var target = AppDeploymentTarget.CreateLive(app.Id, runner.Id);
        target.Status = status;
        target.GenericRuntimeDeployed = true;
        db.WebApps.Add(app);
        db.AppDeploymentTargets.Add(target);
        await db.SaveChangesAsync();

        var auth = new AppRunnerAuthorizationPolicy(db, new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(userId)));
        await new AppRunnerAssignmentPolicy(db, auth, null!).AssignAsync(app, target, runner.Id, userId);

        Assert.That(target.Status, Is.EqualTo(status));
        Assert.That(target.GenericRuntimeDeployed, Is.True);
    }
}
