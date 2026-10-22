using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerStorageVerificationControllerTests
{
    [Test]
    public async Task VerifyStorage_RejectsRunnerBeforeProvisioningCompletes()
    {
        await using var db = CreateDb();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            Status = HostedRunnerStatuses.Provisioning
        };
        db.HostedRunners.Add(hostedRunner);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.VerifyStorage(hostedRunner.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(await db.HostedRunnerStorageVerifications.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task VerifyStorage_AcceptsRunningProvisionedRunner()
    {
        await using var db = CreateDb();
        var infrastructureRunner = CreateOnlineInfrastructureRunner();
        var host = CreateReadyHost(infrastructureRunner);
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            ContainerId = "container-id", Status = HostedRunnerStatuses.Running,
            RunnerHostId = host.Id, RunnerHost = host
        };
        db.HostedRunners.Add(hostedRunner);
        await db.SaveChangesAsync();

        var deployment = new RecordingTrustedActionDeploymentService();
        var controller = CreateController(db, deployment);
        var result = await controller.VerifyStorage(hostedRunner.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<AcceptedResult>());
        Assert.That(deployment.RunnerId, Is.EqualTo(infrastructureRunner.Id));
        Assert.That(deployment.DeploymentTargetId, Is.EqualTo(host.Id));
        Assert.That(deployment.ActionKey, Is.EqualTo(StorageTelemetryActions.MeasureHostedRunnerStorage));
        var verification = await db.HostedRunnerStorageVerifications.SingleAsync();
        Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Pending));
        Assert.That(verification.Reason, Is.EqualTo("AdministratorRequested"));
    }

    [Test]
    public async Task VerifyStorage_RejectsWhenInfrastructureRunnerIsOffline()
    {
        await using var db = CreateDb();
        var infrastructureRunner = CreateOnlineInfrastructureRunner();
        infrastructureRunner.LastHeartbeatAtUtc = DateTime.UtcNow.AddHours(-1);
        var host = CreateReadyHost(infrastructureRunner);
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            ContainerId = "container-id", Status = HostedRunnerStatuses.Running,
            RunnerHostId = host.Id, RunnerHost = host
        };
        db.HostedRunners.Add(hostedRunner);
        await db.SaveChangesAsync();

        var deployment = new RecordingTrustedActionDeploymentService();
        var result = await CreateController(db, deployment).VerifyStorage(hostedRunner.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(deployment.RunnerId, Is.Null);
        Assert.That(await db.HostedRunnerStorageVerifications.CountAsync(), Is.Zero);
    }

    private static AdminHostedRunnerStorageController CreateController(
        ApplicationDbContext db,
        RecordingTrustedActionDeploymentService? deployment = null) =>
        new(db, new NoOpAuditEventService(),
            new HostedRunnerStorageActionDeployment(db, deployment ?? new RecordingTrustedActionDeploymentService()));

    private static Runner CreateOnlineInfrastructureRunner() => new()
    {
        Id = Guid.NewGuid(), Name = "infrastructure", ConnectionId = "infra-connection",
        Purpose = RunnerPurposes.Infrastructure, LastHeartbeatAtUtc = DateTime.UtcNow
    };

    private static RunnerHost CreateReadyHost(Runner infrastructureRunner) => new()
    {
        Id = Guid.NewGuid(), Name = "host", Provider = "test",
        Status = RunnerHostStatuses.Active, ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
        InfrastructureRunnerId = infrastructureRunner.Id, InfrastructureRunner = infrastructureRunner
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingTrustedActionDeploymentService : ITrustedInfrastructureActionDeploymentService
    {
        public Guid? RunnerId { get; private set; }
        public Guid? DeploymentTargetId { get; private set; }
        public string? ActionKey { get; private set; }

        public Task<AppActionModuleInfo> EnsureDeployedAsync(
            Guid runnerId, string actionKey, CancellationToken cancellationToken)
        {
            RunnerId = runnerId;
            ActionKey = actionKey;
            return Task.FromResult(new AppActionModuleInfo(
                actionKey, null, null, "python", "main.py", 120, "sha256:test", false, [], 0, null));
        }

        public Task<AppActionModuleInfo> EnsureDeployedOnTargetAsync(
            Guid runnerId, Guid deploymentTargetId, string actionKey, CancellationToken cancellationToken)
        {
            RunnerId = runnerId;
            DeploymentTargetId = deploymentTargetId;
            ActionKey = actionKey;
            return Task.FromResult(new AppActionModuleInfo(
                actionKey, null, null, "python", "main.py", 120, "sha256:test", false, [], 0, null));
        }
    }
}
