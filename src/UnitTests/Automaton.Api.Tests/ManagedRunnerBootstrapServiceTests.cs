using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestData;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestAssertions;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestServiceFactory;

namespace Automaton.Api.Tests;
[TestFixture]
public class ManagedRunnerBootstrapServiceTests
{
    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsAssignmentInstruction_WhenNoRunnerIsAssigned()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var applicationEvents = new BootstrapRecordingApplicationEventService();
        var service = CreateService(db, applicationEvents);

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsReady, Is.False);
            Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
            Assert.That(result.Message, Is.EqualTo("Assign a Management Runner to this app before deploying."));
            Assert.That(result.RequiresRetry, Is.False);
            Assert.That(db.WebApps.Any(x => x.Id == app.Id), Is.True);
        });
        Assert.That(await db.HostedRunners.AnyAsync(), Is.False);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_DoesNotAutoAssignConnectedHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        var runner = SeedConnectedRunner(db);
        SeedRunningHostedRunner(db, ownerId, planId, runner);

        var service = CreateService(db, new BootstrapRecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.Message, Does.Contain("Assign a Management Runner"));
        Assert.That(await db.AppDeploymentTargets.Where(x => x.WebAppId == app.Id).Select(x => x.ExecutionRunnerId).SingleAsync(), Is.Null);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForSoftDeletedHostedRunnerAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareConnectedHostedRunnerAssignment(db, ownerId, app, deleted: true);

        var service = CreateService(db, new BootstrapRecordingApplicationEventService());

        await AssertUnavailableAndAssignmentAsync(
            db, service, app.Id, ownerId, AppDeployStates.ManagedRuntimeUnavailable,
            "no longer eligible", runnerId, containsMessage: true);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsReady_ForConnectedMachineManagementRunnerWithoutHostedRecord()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareSelfHostedRunnerAssignment(db, ownerId, app, RunnerApplicationProfiles.MachineManagement);
        var service = CreateService(db, new BootstrapRecordingApplicationEventService());
        await AssertReadyForSelfHostedRunnerAsync(db, service, app.Id, ownerId, runnerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsReady_ForConnectedPrivilegedHostSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareSelfHostedRunnerAssignment(
            db, ownerId, app, RunnerApplicationProfiles.PrivilegedHost, privilegedHostApproved: true);
        var service = CreateService(db, new BootstrapRecordingApplicationEventService());
        await AssertReadyForSelfHostedRunnerAsync(db, service, app.Id, ownerId, runnerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsActionableUnavailable_ForOfflineSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        PrepareSelfHostedRunnerAssignment(db, ownerId, app, RunnerApplicationProfiles.Standard, offline: true);

        await AssertUnavailableAsync(
            CreateService(db, new BootstrapRecordingApplicationEventService()),
            app.Id, ownerId, AppDeployStates.ManagedRuntimeUnavailable,
            "The assigned Runner is offline.", containsMessage: false);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForDisabledHostedRunnerAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareConnectedHostedRunnerAssignment(db, ownerId, app, disabled: true);

        await AssertUnavailableAndAssignmentAsync(
            db,
            CreateService(db, new BootstrapRecordingApplicationEventService()),
            app.Id, ownerId, AppDeployStates.ManagedRuntimeUnavailable,
            "no longer eligible", runnerId, containsMessage: true);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_DoesNotAssignOnlineInfrastructureRunnerFromHostedRunnerRecord()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedOnlineInfrastructureRunnerAsHostedRunnerRecord(db, ownerId);

        var service = CreateService(db, new BootstrapRecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);
        Assert.That(result.Message, Does.Contain("Assign a Management Runner"));
        Assert.That(await db.AppDeploymentTargets.Where(x => x.WebAppId == app.Id).Select(x => x.ExecutionRunnerId).SingleAsync(), Is.Null);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForLegacyInfrastructureAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        var runnerId = SeedLegacyInfrastructureAssignment(db, ownerId, app);

        await AssertLegacyInfrastructureAssignmentUnavailableAsync(
            db,
            CreateService(db, new BootstrapRecordingApplicationEventService()),
            app.Id, ownerId, runnerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForMissingPersistedAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedMissingPersistedAssignment(db, ownerId, app);

        var service = CreateService(db, new BootstrapRecordingApplicationEventService());

        await AssertMissingAssignmentUnavailableAsync(db, service, app.Id, ownerId);
    }

}
