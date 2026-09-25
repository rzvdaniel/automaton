using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestData;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestAssertions;
using static Automaton.Api.Tests.ManagedRunnerBootstrapTestServiceFactory;

namespace Automaton.Api.Tests;
[TestFixture]
public class ManagedRunnerBootstrapServiceTests
{
    [Test]
    public async Task EnsureRunnerAssignmentAsync_QueuesProvisioning_WhenManagedRunnerIsNeeded()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);

        var publisher = new BootstrapRecordingEventPublisher();
        var applicationEvents = new BootstrapRecordingApplicationEventService();
        var service = CreateService(db, publisher, applicationEvents);

        await AssertProvisioningQueuedAsync(db, service, publisher, applicationEvents, app.Id, ownerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_AssignsRemoteRuntimePortAndEndpoint()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db, remote: true);

        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());

        await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        var hostedRunner = await db.HostedRunners.SingleAsync();
        Assert.That(hostedRunner.RuntimePort, Is.EqualTo(20000));
        Assert.That(hostedRunner.RuntimeEndpoint, Is.EqualTo("http://203.0.113.10:20000"));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_AssignsExistingConnectedManagedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        var runner = SeedConnectedRunner(db);
        SeedRunningHostedRunner(db, ownerId, planId, runner);

        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo(runner.Id));

        var refreshedTarget = await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault);
        Assert.That(refreshedTarget.ExecutionRunnerId, Is.EqualTo(runner.Id));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_DoesNotSelectProtocolOneHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        PrepareConnectedHostedRunnerAssignment(db, ownerId, app, legacyApi: true, assignToApp: false);

        var publisher = new BootstrapRecordingEventPublisher();
        var service = CreateService(db, publisher, new BootstrapRecordingApplicationEventService());

        await AssertLegacyRunnerIsNotSelectedAsync(db, service, publisher, app.Id, ownerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForSoftDeletedHostedRunnerAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareConnectedHostedRunnerAssignment(db, ownerId, app, deleted: true);

        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());

        await AssertUnavailableAndAssignmentAsync(
            db, service, app.Id, ownerId, AppDeployStates.ManagedRuntimeUnavailable,
            "no longer eligible", runnerId, containsMessage: true);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsReady_ForConnectedStandardSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        var runnerId = PrepareSelfHostedRunnerAssignment(db, ownerId, app, RunnerApplicationProfiles.Standard);
        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());
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
        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());
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
            CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService()),
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
            CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService()),
            app.Id, ownerId, AppDeployStates.ManagedRuntimeUnavailable,
            "no longer eligible", runnerId, containsMessage: true);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_DoesNotAssignOnlineInfrastructureRunnerFromHostedRunnerRecord()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        SeedOnlineInfrastructureRunnerAsHostedRunnerRecord(db, ownerId);

        var publisher = new BootstrapRecordingEventPublisher();
        var service = CreateService(db, publisher, new BootstrapRecordingApplicationEventService());

        await AssertProvisionedInsteadOfLegacyInfrastructureAsync(db, service, publisher, app.Id, ownerId);
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
            CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService()),
            app.Id, ownerId, runnerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForMissingPersistedAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedMissingPersistedAssignment(db, ownerId, app);

        var service = CreateService(db, new BootstrapRecordingEventPublisher(), new BootstrapRecordingApplicationEventService());

        await AssertMissingAssignmentUnavailableAsync(db, service, app.Id, ownerId);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_WhenPlanHasNoManagedRuntime()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: false);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);

        var publisher = new BootstrapRecordingEventPublisher();
        var service = CreateService(db, publisher, new BootstrapRecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRunnerProvisionFailed));
        Assert.That(result.Message, Does.Contain("does not include a hosted runner"));
        Assert.That(publisher.Messages, Is.Empty);
    }
}
