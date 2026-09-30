using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using static Automaton.Api.Tests.InfrastructureWorkflowExecutionServiceTests;

namespace Automaton.Api.Tests;

public sealed class InfrastructureHostValidationReplayTests
{
    [Test]
    public async Task CompletedValidationAcceptsExpiredArtifactWithoutChangingPersistedState()
    {
        await using var db = CreateDbContext();
        var (command, apiKeyId) = await CompleteValidationAsync(db);
        var before = await CaptureStateAsync(db);
        var replay = Result(command) with
        {
            Result = new AppModuleExecutionArtifactResponse(false, null, DateTime.UtcNow.AddDays(-1), "Artifact expired.")
        };

        var outcome = await new InfrastructureHostValidationResultService(db, new RecordingAuditEventService())
            .ApplyAsync(apiKeyId, command.CommandId, replay, CancellationToken.None);

        Assert.That(outcome.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.AlreadyApplied));
        Assert.That(await CaptureStateAsync(db), Is.EqualTo(before));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new InfrastructureHostValidationResultService(db, new RecordingAuditEventService())
                .ApplyAsync(Guid.NewGuid(), command.CommandId, replay, CancellationToken.None));
    }

    private static async Task<(InfrastructureCommandRequestedEvent Command, Guid ApiKeyId)> CompleteValidationAsync(
        ApplicationDbContext db)
    {
        var host = await SeedValidationHostAsync(db);
        var publisher = new RecordingPublisher();
        await CreateWorkflowExecutionService(db, publisher).StartHostValidationAsync(
            new() { RunnerHostId = host.Id }, Guid.NewGuid(), CancellationToken.None);
        var command = publisher.LastCommand!;
        var apiKeyId = host.InfrastructureRunner!.ApiKeyId!.Value;
        var outcome = await new InfrastructureHostValidationResultService(db, new RecordingAuditEventService())
            .ApplyAsync(apiKeyId, command.CommandId, Result(command), CancellationToken.None);
        Assert.That(outcome.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.Accepted));
        return (command, apiKeyId);
    }

    private static async Task<(string Host, string Operation, string Logs)> CaptureStateAsync(ApplicationDbContext db)
    {
        var host = await db.RunnerHosts.AsNoTracking().SingleAsync();
        var logs = await db.InfrastructureWorkflowExecutionLogs.AsNoTracking().OrderBy(log => log.Sequence)
            .Select(log => new { log.Id, log.ExecutionId, log.Sequence, log.Stream, log.Message, log.TimestampUtc })
            .ToArrayAsync();
        var operation = await db.InfrastructureWorkflowExecutions.AsNoTracking().Select(item => new
        {
            item.Status, item.ResultJson, item.Error, item.ExitCode, item.StartedAtUtc, item.CompletedAtUtc
        }).SingleAsync();
        return (JsonSerializer.Serialize(new
        {
            host.MetadataJson, host.HealthJson, host.PrivateIp, host.TotalCpuCores,
            host.TotalMemoryMb, host.TotalDiskMb, host.ValidatedAtUtc, host.UpdatedAt,
            host.ProvisioningStatus, host.ProvisioningFailureCode, host.ProvisioningFailureMessage,
            host.HostedRunnerSchedulingEnabled
        }), JsonSerializer.Serialize(operation), JsonSerializer.Serialize(logs));
    }
}
