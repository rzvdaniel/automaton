using Automaton.Api.Entities;
using Automaton.Api.Config;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public partial class HostedRunnerAuditTests
{
    [Test]
    public async Task InfrastructureActionHandler_StaleConnectionIsRetryableBeforeExecutionClaim()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var host = SeedRunnerHost(db);
        var infrastructure = new Runner { Id = Guid.NewGuid(), Name = "infra", Purpose = RunnerPurposes.Infrastructure, CapabilitiesJson = "[\"HostedRunnerProvisioning\"]", ConnectionId = "stale-connection", ApiInstanceId = "api", LastHeartbeatAtUtc = DateTime.UtcNow, Status = "online" };
        host.InfrastructureRunnerId = infrastructure.Id;
        host.InfrastructureRunner = infrastructure;
        db.Runners.Add(infrastructure);
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = userId, RunnerHostId = host.Id, Name = "hosted", Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, Action = HostedRunnerDeploymentActions.Reprovision, Status = HostedRunnerDeploymentStatuses.Queued, StartedAt = DateTime.UtcNow };
        hosted.Deployments.Add(deployment);
        db.HostedRunners.Add(hosted); db.HostedRunnerDeployments.Add(deployment);
        await db.SaveChangesAsync();
        var publisher = new RecordingPublisher();
        var handler = new InfrastructureActionProvisioningHandler(db, null!, publisher, new RecordingAudit(), null!, new FailingDeploymentService(), Options.Create(new HostedRunnerOptions()), Options.Create(new StorageTelemetryOptions()), NullLogger<InfrastructureActionProvisioningHandler>.Instance);

        Assert.ThrowsAsync<InfrastructureActionConnectionUnavailableException>(() => handler.HandleAsync(new InfrastructureActionExecutionRequestedEvent { OperationId = deployment.Id, HostedRunnerId = hosted.Id, UserId = userId, RunnerHostId = host.Id, ActionKey = "reprovision-hosted-runner", InputJson = "{}", SecretGrantIds = [Guid.NewGuid()] }, CancellationToken.None));
        Assert.That(deployment.Status, Is.Not.EqualTo(HostedRunnerDeploymentStatuses.Failed));
        Assert.That(hosted.Status, Is.Not.EqualTo(HostedRunnerStatuses.Failed));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
        Assert.That(publisher.Messages, Is.Empty);
        Assert.That(await db.HostedRunnerDeployments.Select(x => x.Status).SingleAsync(), Is.Not.EqualTo(HostedRunnerDeploymentStatuses.Failed));
    }

    private sealed class FailingDeploymentService : ITrustedInfrastructureActionDeploymentService
    {
        public Task<AppActionModuleInfo> EnsureDeployedAsync(string connectionId, string actionKey, CancellationToken cancellationToken)
            => throw new HubException($"Connection '{connectionId}' does not exist.");
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<object> Messages { get; } = [];
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class { Messages.Add(message); return Task.CompletedTask; }
    }

    private sealed class RecordingAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
