using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class HostedRunnerRestartLifecycleScenario
{
    public static HostedRunnerRestartSessionSeed Seed(ApplicationDbContext db, Guid userId)
    {
        var now = DateTime.UtcNow;
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "managed-restart",
            ConnectionId = "new-connection",
            Status = RunnerConnectivityStatuses.Online,
            LastHeartbeatAtUtc = now
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerId = runner.Id,
            Name = "managed-restart",
            ContainerName = "managed-restart",
            Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.WaitingForReconnect,
            ExpectedRunnerId = runner.Id,
            PreviousConnectionId = "old-connection",
            ReconnectAfterUtc = now.AddSeconds(-5),
            ReconnectDeadlineUtc = now.AddMinutes(2)
        };
        hostedRunner.Deployments.Add(operation);
        var session = new RunnerConnectionSession
        {
            Id = Guid.NewGuid(),
            RunnerId = runner.Id,
            ConnectionId = runner.ConnectionId,
            ConnectedAtUtc = now.AddSeconds(-10)
        };
        db.AddRange(runner, hostedRunner, session);
        db.SaveChanges();
        return new HostedRunnerRestartSessionSeed(userId, runner.Id, operation.Id, session.Id, now);
    }

    public static async Task<HostedRunnerRestartSessionResult> CompleteAsync(
        ApplicationDbContext db,
        HostedRunnerRestartSessionSeed seed)
    {
        var runner = await db.Runners.SingleAsync(x => x.Id == seed.RunnerId);
        var operation = await db.HostedRunnerDeployments.SingleAsync(x => x.Id == seed.OperationId);
        var session = await db.RunnerConnectionSessions.SingleAsync(x => x.Id == seed.SessionId);
        var runnerService = HostedRunnerProfileTestServiceFactory.CreateWithAudit(db, seed.UserId);

        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);
        var remainedWaiting = operation.Status == HostedRunnerDeploymentStatuses.WaitingForReconnect;
        session.ConnectedAtUtc = seed.StartedAtUtc.AddSeconds(1);
        await db.SaveChangesAsync();
        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);

        var auditCount = await db.AuditEvents.CountAsync(x => x.EventType == AuditEventTypes.RunnerRestartSucceeded);
        return new HostedRunnerRestartSessionResult(
            remainedWaiting,
            operation.Status,
            operation.FinishedAt,
            auditCount);
    }

    public static async Task ApplySuccessfulRestartCommandAsync(
        ApplicationDbContext db,
        HostedRunner runner,
        HostedRunnerDeployment operation)
    {
        var handler = new InfrastructureActionProvisioningHandler(
            db,
            new HostedRunnerAuditNoOpAuditEventService(),
            Microsoft.Extensions.Options.Options.Create(new HostedRunnerOptions { RestartReconnectTimeoutSeconds = 60 }),
            Microsoft.Extensions.Options.Options.Create(new StorageTelemetryOptions()));
        using var result = System.Text.Json.JsonDocument.Parse("{}");
        await handler.ApplySuccessfulOperationAsync(
            runner, operation, "restart-hosted-runner", result.RootElement, CancellationToken.None);
    }
}

internal sealed record HostedRunnerRestartSessionSeed(Guid UserId, Guid RunnerId, Guid OperationId, Guid SessionId, DateTime StartedAtUtc);

internal sealed record HostedRunnerRestartSessionResult(
    bool RemainedWaitingAfterOldSession,
    string Status,
    DateTime? FinishedAt,
    int SuccessfulAuditCount);
