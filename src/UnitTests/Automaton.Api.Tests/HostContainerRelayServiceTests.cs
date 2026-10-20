using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostContainerRelayServiceTests
{
    [Test]
    public async Task Submit_ResolvesStableInfrastructureIdentityAndServerOwnedInputs()
    {
        using var fixture = RelayFixture.Create();
        var result = await fixture.Service.SubmitAsync(fixture.ApiKeyId, fixture.Request(), CancellationToken.None);
        var forwarded = await fixture.ReadForwardedRequestAsync();
        AssertQueueDispatch(fixture, result, forwarded);
    }

    private static void AssertQueueDispatch(
        RelayFixture fixture,
        HostContainerExecutionOperationResponse result,
        HostContainerExecutionRequest forwarded)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Completed, Is.False);
            Assert.That(result.OperationId, Is.EqualTo(fixture.OperationId));
            Assert.That(fixture.Events.Events, Has.Count.EqualTo(1));
            Assert.That(fixture.Events.Events[0].InfrastructureRunnerId, Is.EqualTo(fixture.Infrastructure.Id));
            Assert.That(fixture.Events.Events[0].RunnerHostId, Is.EqualTo(fixture.Host.Id));
            Assert.That(fixture.Events.Events[0].ActionKey, Is.EqualTo(HostContainerExecutionCommandKinds.InfrastructureActionKey));
            Assert.That(forwarded.Scope.HostedRunnerId, Is.EqualTo(fixture.HostedRunner.Id));
            Assert.That(forwarded.Scope.RunnerHostId, Is.EqualTo(fixture.Host.Id));
            Assert.That(forwarded.Resources, Is.Null);
            Assert.That(forwarded.HostPort, Is.Zero);
        });
    }

    [Test]
    public async Task Submit_RejectsAnotherAccountsRunnerBeforePublishing()
    {
        using var fixture = RelayFixture.Create();
        var result = await fixture.Service.SubmitAsync(fixture.OtherAccountApiKeyId, fixture.Request(), CancellationToken.None);

        Assert.That(result.Result?.ErrorCode, Is.EqualTo("hosted_runner_binding_mismatch"));
        Assert.That(fixture.Events.Events, Is.Empty);
        Assert.That(await fixture.Db.HostContainerExecutionOperations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Submit_PreservesStorageAdmissionBeforeQueueing()
    {
        using var fixture = RelayFixture.Create();
        fixture.Host.StorageState = RunnerHostStorageStates.SchedulingBlocked;
        var result = await fixture.Service.SubmitAsync(fixture.ApiKeyId, fixture.Request(), CancellationToken.None);

        Assert.That(result.Result?.ErrorCode, Is.EqualTo("host_storage_reserve_unavailable"));
        Assert.That(fixture.Events.Events, Is.Empty);
    }

    [Test]
    public async Task Submit_MachineRunnerQueuesTypedOperationForAssignedUserWorkspace()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();

        var result = await fixture.Service.SubmitAsync(
            fixture.MachineApiKeyId, fixture.Request(), CancellationToken.None);
        var operation = await fixture.Db.RunnerExecutionOperations.SingleAsync();
        var executionEvent = fixture.Events.RunnerEvents.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Completed, Is.False);
            Assert.That(result.OperationId, Is.EqualTo(fixture.OperationId));
            Assert.That(operation.RunnerId, Is.EqualTo(fixture.MachineRunnerId));
            Assert.That(operation.RequestedByUserId, Is.EqualTo(ownerId));
            Assert.That(operation.ProtectedRequest, Is.Not.Empty);
            Assert.That(executionEvent.OperationId, Is.EqualTo(fixture.OperationId));
            Assert.That(executionEvent.RunnerId, Is.EqualTo(fixture.MachineRunnerId));
            Assert.That(executionEvent.Management, Is.True);
        });
    }

    [Test]
    public async Task Submit_MachineRunnerRejectsWorkspaceWithoutRunnerAssignment()
    {
        using var fixture = RelayFixture.Create();
        fixture.AssignMachineRunnerToPersonalWorkspace(assignRunnerToUser: false);

        var result = await fixture.Service.SubmitAsync(
            fixture.MachineApiKeyId, fixture.Request(), CancellationToken.None);

        Assert.That(result.Result?.ErrorCode, Is.EqualTo("workspace_assignment_missing"));
        Assert.That(fixture.Events.RunnerEvents, Is.Empty);
        Assert.That(await fixture.Db.RunnerExecutionOperations.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task AssignmentAfterWorkspaceRevocationTerminalizesQueuedContainerRequest()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();
        await fixture.Service.SubmitAsync(fixture.MachineApiKeyId, fixture.Request(), CancellationToken.None);
        fixture.Db.RunnerUsers.Remove(await fixture.Db.RunnerUsers.SingleAsync(link => link.RunnerId == fixture.MachineRunnerId));
        await fixture.Db.SaveChangesAsync();

        var assignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);
        var operation = await fixture.RunnerOperations.ReadHostContainerOperationAsync(fixture.MachineApiKeyId,
            fixture.OperationId, ownerId, fixture.MachineRunnerId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("already-applied"));
            Assert.That(assignment.Request, Is.Null);
            Assert.That(operation!.Completed, Is.True);
            Assert.That(operation.Result!.ErrorCode, Is.EqualTo("workspace_assignment_missing"));
        });
    }

    [Test]
    public async Task AssignmentAfterTargetReassignmentTerminalizesQueuedContainerRequest()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();
        await fixture.Service.SubmitAsync(fixture.MachineApiKeyId, fixture.Request(), CancellationToken.None);
        fixture.Target.ExecutionRunnerId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync();

        var assignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);
        var operation = await fixture.RunnerOperations.ReadHostContainerOperationAsync(fixture.MachineApiKeyId,
            fixture.OperationId, ownerId, fixture.MachineRunnerId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("already-applied"));
            Assert.That(assignment.Request, Is.Null);
            Assert.That(operation!.Completed, Is.True);
            Assert.That(operation.Result!.ErrorCode, Is.EqualTo("app_assignment_mismatch"));
        });
    }

    [Test]
    public async Task AssignmentAfterCapabilityRevocationTerminalizesQueuedContainerRequest()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();
        await fixture.Service.SubmitAsync(fixture.MachineApiKeyId, fixture.Request(), CancellationToken.None);
        fixture.MachineRunner.CapabilitiesJson = "[\"ApplicationHosting\"]";
        await fixture.Db.SaveChangesAsync();

        var assignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);
        var operation = await fixture.RunnerOperations.ReadHostContainerOperationAsync(fixture.MachineApiKeyId,
            fixture.OperationId, ownerId, fixture.MachineRunnerId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("already-applied"));
            Assert.That(assignment.Request, Is.Null);
            Assert.That(operation!.Completed, Is.True);
            Assert.That(operation.Result!.ErrorCode, Is.EqualTo("machine_container_capability_missing"));
        });
    }

    [Test]
    public async Task MachineBuildAssignmentRechecksActiveAttemptAndCarriesServerDerivedLimits()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();
        var attemptId = Guid.NewGuid();
        fixture.Target.ActiveDeploymentExecutionAttemptId = attemptId;
        await fixture.Db.SaveChangesAsync();
        var request = fixture.BuildRequest(attemptId);
        await fixture.Service.SubmitAsync(fixture.MachineApiKeyId, request, CancellationToken.None);
        var initialAssignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);
        fixture.Target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync();

        var assignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);
        var terminal = await fixture.RunnerOperations.ReadHostContainerOperationAsync(fixture.MachineApiKeyId,
            fixture.OperationId, ownerId, fixture.MachineRunnerId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("already-applied"));
            Assert.That(assignment.Request, Is.Null);
            Assert.That(terminal!.Result!.ErrorCode, Is.EqualTo("deployment_attempt_mismatch"));
            Assert.That(initialAssignment.Disposition, Is.EqualTo("execute"));
            Assert.That(initialAssignment.Request!.HostContainer!.ContainerLimits,
                Is.EqualTo(new HostContainerResourceLimits(1.0m, 1024, 256)));
            Assert.That(initialAssignment.Request.HostContainer.MinimumFreeDiskMb, Is.EqualTo(1024));
        });
    }

    [Test]
    public async Task MachineComposeAssignmentCarriesServerDerivedPerContainerLimits()
    {
        using var fixture = RelayFixture.Create();
        var ownerId = fixture.AssignMachineRunnerToPersonalWorkspace();
        var attemptId = Guid.NewGuid();
        fixture.Target.ActiveDeploymentExecutionAttemptId = attemptId;
        await fixture.Db.SaveChangesAsync();
        var request = new HostContainerExecutionRequest
        {
            OperationId = fixture.OperationId,
            Operation = HostContainerOperationKinds.ApplyComposeProject,
            CandidateId = Guid.NewGuid(),
            Compose = new ApplicationComposeSpec
            {
                PublicService = "web",
                Services = [new() { Name = "web", Image = "nginx:1.27", Port = 8080 }]
            },
            Scope = new(Guid.Empty, Guid.Empty, fixture.App.Id, fixture.Target.Id, attemptId)
        };

        await fixture.Service.SubmitAsync(fixture.MachineApiKeyId, request, CancellationToken.None);
        var assignment = await fixture.RunnerOperations.AssignmentAsync(fixture.MachineApiKeyId,
            fixture.OperationId, CancellationToken.None, authenticatedUserId: ownerId, runnerId: fixture.MachineRunnerId);

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Disposition, Is.EqualTo("execute"));
            Assert.That(assignment.Request!.HostContainer!.ContainerLimits,
                Is.EqualTo(new HostContainerResourceLimits(1.0m, 1024, 256)));
            Assert.That(assignment.Request.HostContainer.MinimumFreeDiskMb, Is.EqualTo(1024));
            Assert.That(assignment.Request.HostContainer.Compose!.PublicService, Is.EqualTo("web"));
        });
    }

    [Test]
    public async Task MachineContainerDispatchFailsClosedWhenResourcePolicyIsInvalid()
    {
        using var fixture = RelayFixture.Create();
        fixture.AssignMachineRunnerToPersonalWorkspace();
        fixture.MachineResources.MinimumFreeDiskMb = 0;
        var attemptId = Guid.NewGuid();
        fixture.Target.ActiveDeploymentExecutionAttemptId = attemptId;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.SubmitAsync(
            fixture.MachineApiKeyId, fixture.BuildRequest(attemptId), CancellationToken.None);

        Assert.That(result.Result?.ErrorCode, Is.EqualTo("machine_resource_policy_unavailable"));
        Assert.That(fixture.Events.RunnerEvents, Is.Empty);
        Assert.That(await fixture.Db.RunnerExecutionOperations.CountAsync(), Is.Zero);
    }

    private sealed class RelayFixture : IDisposable
    {
        public ApplicationDbContext Db { get; }
        public HostContainerRelayService Service { get; }
        public RunnerExecutionOperationService RunnerOperations { get; private set; } = null!;
        public MachineContainerResourceOptions MachineResources { get; } = new();
        public RecordingPublisher Events { get; } = new();
        public Guid ApiKeyId { get; } = Guid.NewGuid();
        public Guid OtherAccountApiKeyId { get; } = Guid.NewGuid();
        public Guid MachineApiKeyId { get; } = Guid.NewGuid();
        public Guid OperationId { get; } = Guid.NewGuid();
        public Guid MachineRunnerId { get; } = Guid.NewGuid();
        public HostedRunner HostedRunner { get; private set; } = null!;
        public RunnerHost Host { get; private set; } = null!;
        public Runner Infrastructure { get; private set; } = null!;
        public WebApp App { get; private set; } = null!;
        public AppDeploymentTarget Target { get; private set; } = null!;
        public Runner MachineRunner { get; private set; } = null!;

        private RelayFixture()
        {
            Db = CreateDatabase();
            SeedDatabase();
            Service = CreateService();
        }

        private static ApplicationDbContext CreateDatabase()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        private void SeedDatabase()
        {
            var hostedRunnerIdentity = new Runner
            {
                Id = Guid.NewGuid(), Name = "hosted", ApiKeyId = ApiKeyId, Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.Standard, ConnectionId = "hosted-online"
            };
            Host = new RunnerHost
            {
                Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = Guid.NewGuid(),
                StorageState = RunnerHostStorageStates.Healthy,
                StorageAvailableBytes = 20L * 1024 * 1024 * 1024,
                StorageReserveRequiredBytes = 5L * 1024 * 1024 * 1024,
                StorageMeasuredAtUtc = DateTime.UtcNow
            };
            Infrastructure = new Runner
            {
                Id = Host.InfrastructureRunnerId!.Value, Name = "infra", Purpose = RunnerPurposes.Infrastructure,
                ConnectionId = "infra-online", LastHeartbeatAtUtc = DateTime.UtcNow,
                ApiCompatibilityVersion = RunnerApiCompatibilityVersions.HostContainerExecution,
                CapabilitiesJson = "[\"HostContainerExecution\"]", ApiInstanceId = "different-api-slot"
            };
            HostedRunner = new HostedRunner
            {
                Id = Guid.NewGuid(), RunnerId = hostedRunnerIdentity.Id, Runner = hostedRunnerIdentity,
                RunnerHostId = Host.Id, RunnerHost = Host, Name = "hosted", ContainerName = "hosted",
                UserId = Guid.NewGuid(),
                SpecificationSnapshot = new() { Resources = new() { SharedMemoryMb = 96, DockerLogMaxSizeMb = 7, DockerLogMaxFiles = 2 } }
            };
            App = new WebApp { Id = Guid.NewGuid(), Name = "app", Summary = "", Description = "", Definition = "" };
            Target = AppDeploymentTarget.CreateLive(App.Id, hostedRunnerIdentity.Id);
            Db.RunnerHosts.Add(Host);
            Db.Runners.AddRange(hostedRunnerIdentity, Infrastructure, new Runner
            {
                Id = Guid.NewGuid(), Name = "other-account-hosted", ApiKeyId = OtherAccountApiKeyId,
                Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.Standard
            });
            Db.HostedRunners.Add(HostedRunner);
            Db.WebApps.Add(App);
            Db.AppDeploymentTargets.Add(Target);
            Db.SaveChanges();
        }

        private HostContainerRelayService CreateService()
        {
            RunnerOperations = RunnerExecutionTestServices.Operations(Db, Events);
            var operations = new HostContainerExecutionOperationService(Db, Events, RunnerOperations);
            return new HostContainerRelayService(
                new HostContainerRelayStateResolver(Db), operations, RunnerOperations,
                Options.Create(new StorageTelemetryOptions()), Options.Create(MachineResources));
        }

        public static RelayFixture Create() => new();

        public HostContainerExecutionRequest Request() => new()
        {
            OperationId = OperationId,
            Operation = HostContainerOperationKinds.ResolveImage,
            RequestedImage = "python:3.13-slim",
            Scope = new(Guid.Empty, Guid.Empty, App.Id, Target.Id)
        };

        public HostContainerExecutionRequest BuildRequest(Guid attemptId) => new()
        {
            OperationId = OperationId,
            Operation = HostContainerOperationKinds.RunBuild,
            RequestedImage = "node:22-slim",
            TimeoutSeconds = 300,
            Command = new() { Executable = "npm", Arguments = ["run", "build"] },
            Scope = new(Guid.Empty, Guid.Empty, App.Id, Target.Id, attemptId)
        };

        public Guid AssignMachineRunnerToPersonalWorkspace(bool assignRunnerToUser = true)
        {
            var ownerId = Guid.NewGuid();
            MachineRunner = new Runner
            {
                Id = MachineRunnerId,
                Name = "machine-management",
                ApiKeyId = MachineApiKeyId,
                Purpose = RunnerPurposes.Managed,
                ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
                CapabilitiesJson = "[\"ApplicationHosting\",\"ManagedContainerExecution\"]"
            };
            Target.ExecutionRunnerId = MachineRunner.Id;
            Db.Runners.Add(MachineRunner);
            Db.WebAppUsers.Add(new WebAppUser { WebAppId = App.Id, UserId = ownerId });
            if (assignRunnerToUser)
                Db.RunnerUsers.Add(new RunnerUser { RunnerId = MachineRunner.Id, UserId = ownerId });
            Db.SaveChanges();
            return ownerId;
        }

        public async Task<HostContainerExecutionRequest> ReadForwardedRequestAsync()
        {
            var published = Events.Events.Single();
            return System.Text.Json.JsonSerializer.Deserialize<HostContainerExecutionRequest>(published.InputJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<InfrastructureCommandRequestedEvent> Events { get; } = [];
        public List<RunnerExecutionRequestedEvent> RunnerEvents { get; } = [];

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            if (message is InfrastructureCommandRequestedEvent command) Events.Add(command);
            if (message is RunnerExecutionRequestedEvent runnerOperation) RunnerEvents.Add(runnerOperation);
            return Task.CompletedTask;
        }
    }
}
