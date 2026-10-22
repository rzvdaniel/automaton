using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerConnectionAssignmentTests
{
    private ApplicationDbContext db = null!;
    private RunnerService runners = null!;
    private Guid ownerId;
    private Guid runnerId;
    private Guid previousRunnerId;
    private Guid hostedRunnerId;

    [SetUp]
    public void SetUp()
    {
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ownerId = Guid.NewGuid();
        runnerId = Guid.NewGuid();
        previousRunnerId = Guid.NewGuid();
        hostedRunnerId = Guid.NewGuid();
        db.Runners.AddRange(
            new Runner
            {
                Id = runnerId, Name = "current", Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.Standard,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.ManagedContainerExecution],
                    RunnerPurposes.Managed)
            },
            new Runner { Id = previousRunnerId, Name = "previous", Purpose = RunnerPurposes.Managed });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runnerId, UserId = ownerId });
        db.HostedRunners.Add(new HostedRunner
        {
            Id = hostedRunnerId, UserId = ownerId, RunnerId = runnerId,
            Name = "historical", ContainerName = "historical-container", Status = HostedRunnerStatuses.Running
        });
        AddHistoricalEnrollment(previousRunnerId);
        AddHistoricalEnrollment(runnerId);
        db.SaveChanges();

        var userContext = new UserContextService(new HttpContextAccessor());
        var authorization = new ResourceAuthorizationService(db, userContext);
        runners = ManagedRunnerConnectionTestServiceFactory.Create(db, userContext, authorization);
    }

    [TearDown]
    public void TearDown() => db.Dispose();

    [TestCase(RunnerApplicationProfiles.Standard)]
    [TestCase(RunnerApplicationProfiles.MachineManagement)]
    public async Task Reconnect_DoesNotAssignUnassignedApp(string profile)
    {
        (await db.Runners.SingleAsync(item => item.Id == runnerId)).ApplicationProfile = profile;
        var target = AddApp(null);
        await db.SaveChangesAsync();

        await ReconnectAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(item => item.Id == target.Id);
        Assert.That(persisted.ExecutionRunnerId, Is.Null);
        Assert.That(persisted.Status, Is.EqualTo(DeploymentTargetStatus.Unassigned));
        Assert.That(persisted.GenericRuntimeDeployed, Is.False);
    }

    [Test]
    public async Task Reconnect_DoesNotRebindAppFromHostedEnrollmentHistory()
    {
        var target = AddApp(previousRunnerId);
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await db.SaveChangesAsync();

        await ReconnectAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(item => item.Id == target.Id);
        Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(previousRunnerId));
        Assert.That(persisted.Status, Is.EqualTo(DeploymentTargetStatus.Running));
        Assert.That(persisted.GenericRuntimeDeployed, Is.True);
    }

    [TestCase(RunnerApplicationProfiles.Standard)]
    [TestCase(RunnerApplicationProfiles.MachineManagement)]
    public async Task Reconnect_PreservesAssignedRuntimeAndUpdatesPresence(string profile)
    {
        (await db.Runners.SingleAsync(item => item.Id == runnerId)).ApplicationProfile = profile;
        var target = AddApp(runnerId);
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await db.SaveChangesAsync();

        await ReconnectAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(item => item.Id == target.Id);
        var runner = await db.Runners.SingleAsync(item => item.Id == runnerId);
        Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(runnerId));
        Assert.That(persisted.Status, Is.EqualTo(DeploymentTargetStatus.Running));
        Assert.That(persisted.GenericRuntimeDeployed, Is.True);
        Assert.That(runner.ConnectionId, Is.EqualTo("reconnected"));
        Assert.That(runner.Status, Is.EqualTo(RunnerConnectivityStatuses.Online));
        Assert.That(runner.LastHeartbeatAtUtc, Is.Not.Null);
        Assert.That(await db.ApplicationEvents.AnyAsync(item => item.AppId == target.WebAppId), Is.True);
    }

    private Task ReconnectAsync() => runners.UpdateConnection(runnerId, ownerId, new RunnerDetails
    {
        Id = runnerId, Name = "current", ConnectionId = "reconnected", HostedRunnerId = hostedRunnerId,
        ApiCompatibilityVersion = RunnerApiCompatibilityVersions.ContainerApplicationExecution
    }, CancellationToken.None);

    private void AddHistoricalEnrollment(Guid enrolledRunnerId) => db.RunnerEnrollments.Add(new RunnerEnrollment
    {
        Id = Guid.NewGuid(), HostedRunnerId = hostedRunnerId, RunnerId = enrolledRunnerId,
        RunnerHostId = Guid.NewGuid(), Status = RunnerEnrollmentStatuses.Completed,
        UsedAtUtc = DateTime.UtcNow, InstallationId = Guid.NewGuid().ToString("N")
    });

    private AppDeploymentTarget AddApp(Guid? assignedRunnerId)
    {
        var app = new WebApp
        {
            Id = Guid.NewGuid(), Name = "app", Summary = "", Description = "", Definition = "{}",
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
            WebAppUsers = new List<WebAppUser>(),
            ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
            {
                Runtime = new ApplicationRuntimeSpec
                {
                    Image = "test/runtime:1", Command = new ApplicationCommandSpec { Executable = "server" }
                }
            })
        };
        app.WebAppUsers.Add(new WebAppUser { WebAppId = app.Id, UserId = ownerId });
        var target = AppDeploymentTarget.CreateLive(app.Id, assignedRunnerId);
        app.DeploymentTargets.Add(target);
        db.WebApps.Add(app);
        return target;
    }
}
