using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        Assert.That(response.ResultJson, Does.Contain("dockerStatus"));
        Assert.That(await db.InfrastructureWorkflowExecutionLogs.CountAsync(), Is.EqualTo(2));
        Assert.That(executor.LastScript, Does.Contain("test-network"));
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
                "{\"operatingSystem\":\"Linux\",\"dockerStatus\":\"running\"}\n",
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
}
