using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

        var publisher = new RecordingEventPublisher();
        var applicationEvents = new RecordingApplicationEventService();
        var service = CreateService(db, publisher, applicationEvents);

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.PreparingManagedRuntime));
        Assert.That(result.RequiresRetry, Is.True);
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>().Count(), Is.EqualTo(1));
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>().Single().OperationId, Is.Not.EqualTo(Guid.Empty));

        var hostedRunner = await db.HostedRunners.SingleAsync();
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(hostedRunner.RunnerHostId, Is.Not.Null);
        Assert.That(applicationEvents.Events.Select(x => x.EventType), Does.Contain(ApplicationEventTypes.ManagedRuntimePreparing));
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

        var service = CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService());

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

        var service = CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo(runner.Id));

        var refreshedTarget = await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault);
        Assert.That(refreshedTarget.ExecutionRunnerId, Is.EqualTo(runner.Id));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForSoftDeletedHostedRunnerAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        var runner = SeedConnectedRunner(db);
        SeedRunningHostedRunner(db, ownerId, planId, runner);
        var hostedRunner = db.HostedRunners.Single();
        hostedRunner.DeletedAt = DateTime.UtcNow;
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();

        var service = CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("no longer eligible"));
        Assert.That((await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault)).ExecutionRunnerId, Is.EqualTo(runner.Id));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsReady_ForConnectedStandardSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId: null);
        var runner = SeedSelfHostedRunner(db, ownerId, RunnerApplicationProfiles.Standard);
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();

        var result = await CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService())
            .EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo(runner.Id));
        Assert.That(await db.HostedRunners.AnyAsync(), Is.False);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsReady_ForConnectedPrivilegedHostSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId: null);
        var runner = SeedSelfHostedRunner(db, ownerId, RunnerApplicationProfiles.PrivilegedHost);
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();

        var result = await CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService())
            .EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo(runner.Id));
        Assert.That(await db.HostedRunners.AnyAsync(), Is.False);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsActionableUnavailable_ForOfflineSelfHostedRunner()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId: null);
        var runner = SeedSelfHostedRunner(db, ownerId, RunnerApplicationProfiles.Standard);
        runner.ConnectionId = null;
        runner.LastHeartbeatAtUtc = DateTime.UtcNow.AddMinutes(-5);
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();

        var result = await CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService())
            .EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Is.EqualTo("The assigned Runner is offline."));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForDisabledHostedRunnerAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        var runner = SeedConnectedRunner(db);
        SeedRunningHostedRunner(db, ownerId, planId, runner);
        db.HostedRunners.Single().Status = HostedRunnerStatuses.Disabled;
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = runner.Id;
        db.SaveChanges();

        var result = await CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService())
            .EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("no longer eligible"));
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
        var infrastructureRunner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-runner",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure),
            ConnectionId = "infrastructure-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online"
        };
        db.Runners.Add(infrastructureRunner);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            RunnerId = infrastructureRunner.Id,
            Runner = infrastructureRunner,
            Name = "Legacy Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "legacy-hosted-runner",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var publisher = new RecordingEventPublisher();
        var service = CreateService(db, publisher, new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(publisher.Messages.OfType<ManagedRunnerProvisionRequestedEvent>(), Is.Empty);
        Assert.That((await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault)).ExecutionRunnerId, Is.Null);
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
        var infrastructureRunner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-runner",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure),
            ConnectionId = "infrastructure-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online"
        };
        db.Runners.Add(infrastructureRunner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = infrastructureRunner.Id, UserId = ownerId });
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = infrastructureRunner.Id;
        db.SaveChanges();

        var service = CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("Infrastructure Runners"));
        Assert.That((await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault)).ExecutionRunnerId, Is.EqualTo(infrastructureRunner.Id));
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_ForMissingPersistedAssignment()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: true);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);
        SeedRunnerHost(db);
        app.DeploymentTargets.Single(target => target.IsDefault).ExecutionRunnerId = Guid.NewGuid();
        db.SaveChanges();

        var service = CreateService(db, new RecordingEventPublisher(), new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRuntimeUnavailable));
        Assert.That(result.Message, Does.Contain("not found"));
        Assert.That((await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault)).ExecutionRunnerId, Is.Not.Null);
    }

    [Test]
    public async Task EnsureRunnerAssignmentAsync_ReturnsUnavailable_WhenPlanHasNoManagedRuntime()
    {
        using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var planId = SeedHostedRunnerPlan(db, enabled: false);
        var app = SeedBackendApp(db, ownerId);
        SeedUser(db, ownerId, planId);

        var publisher = new RecordingEventPublisher();
        var service = CreateService(db, publisher, new RecordingApplicationEventService());

        var result = await service.EnsureRunnerAssignmentAsync(app.Id, ownerId);

        Assert.That(result.IsReady, Is.False);
        Assert.That(result.State, Is.EqualTo(AppDeployStates.ManagedRunnerProvisionFailed));
        Assert.That(result.Message, Does.Contain("does not include a hosted runner"));
        Assert.That(publisher.Messages, Is.Empty);
    }

    private static ManagedRunnerBootstrapService CreateService(
        ApplicationDbContext db,
        RecordingEventPublisher publisher,
        RecordingApplicationEventService applicationEvents)
    {
        var provisioningService = new HostedRunnerProvisioningService(
            db,
            TestRunnerEnrollmentFactory.Create(db, new NoOpAuditEventService()),
            publisher,
            new TestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
            {
                Enabled = true,
                RuntimePort = 8080
            }),
            new HostedRunnerSpecificationResolver(db),
            new NoOpAuditEventService(),
            NullLogger<HostedRunnerProvisioningService>.Instance);
        var userContextService = new UserContextService(new HttpContextAccessor());
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var runnerAuthorizationPolicy = new AppRunnerAuthorizationPolicy(db, authorizationService);
        var runnerAssignmentPolicy = new AppRunnerAssignmentPolicy(
            db,
            new HostedRunnerAppCapacityService(db),
            runnerAuthorizationPolicy,
            new AppDeploymentTargetResolver(db, authorizationService, runnerAuthorizationPolicy));

        return new ManagedRunnerBootstrapService(
            db,
            provisioningService,
            publisher,
            applicationEvents,
            runnerAssignmentPolicy);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedUser(ApplicationDbContext db, Guid userId, Guid? planId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{userId:N}@automaton.test",
            Email = $"user-{userId:N}@automaton.test",
            PublicUsername = $"user-{userId:N}",
            SubscriptionPlanId = planId,
        });
        db.SaveChanges();
    }

    private static WebApp SeedBackendApp(ApplicationDbContext db, Guid ownerId)
    {
        var app = new WebApp
        {
            Id = Guid.NewGuid(),
            Name = "Backend App",
            Summary = "Summary",
            Description = "Description",
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        db.WebApps.Add(app);
        db.WebAppUsers.Add(new WebAppUser
        {
            WebAppId = app.Id,
            UserId = ownerId
        });
        db.SaveChanges();
        return app;
    }

    private static Guid SeedHostedRunnerPlan(ApplicationDbContext db, bool enabled)
    {
        var specification = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = "test-hosted",
            Name = "Test Hosted Runner",
            IsActive = true
        };
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = enabled ? "Hosted" : "No Hosted",
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = enabled,
                    Visibility = HostedRunnerVisibility.Public,
                    SpecificationId = specification.Id
                }
            })
        };

        db.HostedRunnerSpecifications.Add(specification);
        db.SubscriptionPlans.Add(plan);
        db.SaveChanges();
        return plan.Id;
    }

    private static RunnerHost SeedRunnerHost(ApplicationDbContext db, bool remote = false)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-a",
            Provider = remote ? RunnerHostProviders.Hetzner : RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = remote
                ? RunnerHostRuntimeConnectivityModes.PublicHostPort
                : RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            PublicIp = remote ? "203.0.113.10" : null,
            InfrastructureRunnerId = Guid.NewGuid(),
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow
        };

        db.RunnerHosts.Add(host);
        db.SaveChanges();
        return host;
    }

    private static Runner SeedConnectedRunner(ApplicationDbContext db)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-runner",
            ConnectionId = "conn-1",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online"
        };

        db.Runners.Add(runner);
        db.SaveChanges();
        return runner;
    }

    private static Runner SeedSelfHostedRunner(ApplicationDbContext db, Guid ownerId, string applicationProfile)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = $"self-hosted-{applicationProfile}",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = applicationProfile,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                RunnerPurposes.Managed),
            ConnectionId = "self-hosted-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            LastConnectedAtUtc = DateTime.UtcNow,
            Status = "online"
        };

        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        db.SaveChanges();
        return runner;
    }

    private static void SeedRunningHostedRunner(ApplicationDbContext db, Guid ownerId, Guid planId, Runner runner)
    {
        var host = SeedRunnerHost(db);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            RunnerHostId = host.Id,
            RunnerHost = host,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "automaton-hosted-runner-test",
            RuntimeEndpoint = "http://automaton-hosted-runner-test:8001",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = ownerId });
        db.SaveChanges();
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingApplicationEventService : IApplicationEventService
    {
        public List<ApplicationEventWriteRequest> Events { get; } = [];

        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            Events.Add(request);
            return Task.CompletedTask;
        }

        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(
            Guid appId,
            ApplicationEventQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<ApplicationEventDto>
            {
                Items = [],
                TotalCount = 0,
                Page = query.Page,
                PageSize = query.PageSize
            });
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeHostedRunnerRemoteCommandExecutor
    {
        public bool CanHandle(RunnerHost host, HostedRunnerOptions options) => true;

        public Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HostedRunnerRemoteExecutionResult(true, 0, "ok", string.Empty));

        public Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HostedRunnerRemoteExecutionResult(true, 0, "ok", string.Empty));
    }

    private sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T>
        where T : class
    {
        public T Value { get; } = value;

        public T Get(string? name) => Value;
    }
}
