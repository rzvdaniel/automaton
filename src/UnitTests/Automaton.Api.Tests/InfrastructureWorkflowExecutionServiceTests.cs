using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Tests;

public sealed class InfrastructureWorkflowExecutionServiceTests
{
    [Test]
    public async Task HostValidation_PersistsResultLogsAndTargetIdentity()
    {
        await using var db = CreateDbContext();
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure), RunnerPurposes.Infrastructure),
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            InfrastructureRunnerId = runner.Id,
            InfrastructureRunner = runner
        };
        db.Runners.Add(runner);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var executor = new FakeInfrastructureRunnerCommandExecutor();
        var service = new InfrastructureWorkflowExecutionService(
            db,
            executor,
            new RecordingAuditEventService(),
            Options.Create(new HostedRunnerOptions { DockerNetwork = "test-network" }),
            NullLogger<InfrastructureWorkflowExecutionService>.Instance);

        var response = await service.StartHostValidationAsync(
            new StartHostValidationRequest { RunnerHostId = host.Id, IdempotencyKey = "validation-1" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.That(response.Status, Is.EqualTo(InfrastructureWorkflowExecutionStatuses.Succeeded));
        Assert.That(response.TargetRunnerId, Is.EqualTo(runner.Id));
        Assert.That(response.ResultJson, Does.Contain("\"health\""));
        var validatedHost = await db.RunnerHosts.FindAsync(host.Id);
        Assert.That(validatedHost!.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.AwaitingApproval));
        Assert.That(validatedHost.ValidatedAtUtc, Is.Not.Null);
        Assert.That(validatedHost.IsSchedulingEligible, Is.False);
        Assert.That(await db.InfrastructureWorkflowExecutionLogs.CountAsync(), Is.EqualTo(2));
        Assert.That(executor.LastScript, Does.Contain("test-network"));
        Assert.That(executor.LastScript, Does.Contain("automaton-host-exec"));
    }

    [Test]
    public async Task HostApproval_EnablesSchedulingOnlyAfterValidationAndOnlineRunner()
    {
        await using var db = CreateDbContext();
        var apiKey = new ApiKey { Id = Guid.NewGuid(), Name = "infra", HashedKey = "hash", KeyPrefix = "ak_runner_test", CreatedAt = DateTime.UtcNow };
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            ApiKey = apiKey,
            ApiKeyId = apiKey.Id,
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = "203.0.113.10",
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
            ProvisioningStatus = RunnerHostProvisioningStatuses.AwaitingApproval,
            ValidatedAtUtc = DateTime.UtcNow,
            HostedRunnerSchedulingEnabled = false,
            InfrastructureRunner = runner,
            InfrastructureRunnerId = runner.Id
        };
        db.ApiKeys.Add(apiKey);
        db.Runners.Add(runner);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var service = new InfrastructureRunnerHostApprovalService(
            db,
            new RecordingAuditEventService(),
            NullLogger<InfrastructureRunnerHostApprovalService>.Instance,
            new NullHubContext<AutomatonHub>());

        var approved = await service.ApproveAsync(host.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.That(approved.ProvisioningStatus, Is.EqualTo(RunnerHostProvisioningStatuses.Ready));
        Assert.That(approved.HostedRunnerSchedulingEnabled, Is.True);
        Assert.That(approved.ApprovedAtUtc, Is.Not.Null);
        Assert.That(approved.ApprovedByUserId, Is.Not.Null);
        Assert.That(approved.IsSchedulingEligible, Is.True);
    }

    [Test]
    public void HostApproval_RejectsPublicConnectivityWithoutPublicAddress()
    {
        using var db = CreateDbContext();
        var apiKey = new ApiKey { Id = Guid.NewGuid(), Name = "infra", HashedKey = "hash", KeyPrefix = "ak_runner_test", CreatedAt = DateTime.UtcNow };
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            ApiKey = apiKey,
            ApiKeyId = apiKey.Id,
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
            ProvisioningStatus = RunnerHostProvisioningStatuses.AwaitingApproval,
            ValidatedAtUtc = DateTime.UtcNow,
            HostedRunnerSchedulingEnabled = false,
            InfrastructureRunner = runner,
            InfrastructureRunnerId = runner.Id
        };
        db.ApiKeys.Add(apiKey);
        db.Runners.Add(runner);
        db.RunnerHosts.Add(host);
        db.SaveChanges();

        var service = new InfrastructureRunnerHostApprovalService(
            db,
            new RecordingAuditEventService(),
            NullLogger<InfrastructureRunnerHostApprovalService>.Instance,
            new NullHubContext<AutomatonHub>());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(host.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("public IPv4"));
        Assert.That(host.HostedRunnerSchedulingEnabled, Is.False);
    }

    [Test]
    public async Task HostValidation_ReusesExecutionForIdempotencyKey()
    {
        await using var db = CreateDbContext();
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infra-test",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure), RunnerPurposes.Infrastructure),
            ConnectionId = "connection",
            LastHeartbeatAtUtc = DateTime.UtcNow
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-test",
            Provider = RunnerHostProviders.Hetzner,
            InfrastructureRunnerId = runner.Id,
            InfrastructureRunner = runner
        };
        db.Runners.Add(runner);
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var executor = new FakeInfrastructureRunnerCommandExecutor();
        var service = new InfrastructureWorkflowExecutionService(
            db,
            executor,
            new RecordingAuditEventService(),
            Options.Create(new HostedRunnerOptions()),
            NullLogger<InfrastructureWorkflowExecutionService>.Instance);
        var request = new StartHostValidationRequest { RunnerHostId = host.Id, IdempotencyKey = "same-request" };

        var first = await service.StartHostValidationAsync(request, Guid.NewGuid(), CancellationToken.None);
        var second = await service.StartHostValidationAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(executor.CallCount, Is.EqualTo(1));
        Assert.That(await db.InfrastructureWorkflowExecutions.CountAsync(), Is.EqualTo(1));
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"infrastructure-workflow-{Guid.NewGuid()}")
            .Options);

    private sealed class FakeInfrastructureRunnerCommandExecutor : IInfrastructureRunnerCommandExecutor
    {
        public int CallCount { get; private set; }
        public string LastScript { get; private set; } = string.Empty;

        public Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastScript = script;
            return Task.FromResult(new HostedRunnerRemoteExecutionResult(
                true,
                0,
                "{\"totalCpuCores\":2,\"totalMemoryMb\":4096,\"totalDiskMb\":40960,\"metadata\":{\"hostname\":\"host-test\"},\"health\":{\"docker\":\"Healthy\",\"network\":\"Healthy\",\"disk\":\"Healthy\",\"apiConnectivity\":\"Healthy\"}}\n",
                "validation started\n"));
        }
    }

    private sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NullHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }

    private sealed class NullHubClients : IHubClients
    {
        public IClientProxy All => new NullClientProxy();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new NullClientProxy();
        public IClientProxy Client(string connectionId) => new NullClientProxy();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new NullClientProxy();
        public IClientProxy Group(string groupName) => new NullClientProxy();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new NullClientProxy();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new NullClientProxy();
        public IClientProxy User(string userId) => new NullClientProxy();
        public IClientProxy Users(IReadOnlyList<string> userIds) => new NullClientProxy();
    }

    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
