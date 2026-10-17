using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class MachineHostContainerOperationTests
{
    [Test]
    public async Task RetryWithEquivalentPayloadIsIdempotentAndLostOperationIsReadable()
    {
        await using var scenario = await MachineHostContainerOperationScenario.CreateAsync();
        await scenario.QueueWithEquivalentRetryAsync();
        var response = await scenario.CompleteAsLostAsync();

        Assert.Multiple(() =>
        {
            Assert.That(scenario.OperationCount, Is.EqualTo(1));
            Assert.That(response?.Completed, Is.True);
            Assert.That(response?.Result?.ErrorCode, Is.EqualTo("operation_lost"));
        });
    }
}

internal sealed class MachineHostContainerOperationScenario(ApplicationDbContext db, Runner runner,
    RunnerExecutionOperationService service, Guid operationId, Guid actorId, HostContainerExecutionRequest request)
    : IAsyncDisposable
{
    public int OperationCount => db.RunnerExecutionOperations.Count();

    public static async Task<MachineHostContainerOperationScenario> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var runner = new Runner
        {
            Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "machine",
            Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            LastHeartbeatAtUtc = DateTime.UtcNow, RunnerUsers = []
        };
        db.Runners.Add(runner);
        await db.SaveChangesAsync();
        var service = RunnerExecutionTestServices.Operations(db);
        var operationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var request = new HostContainerExecutionRequest
        {
            OperationId = operationId,
            Scope = new(Guid.Empty, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), MachineRunnerId: runner.Id),
            Operation = HostContainerOperationKinds.RemoveTargetContainers
        };
        return new(db, runner, service, operationId, actorId, request);
    }

    public async Task QueueWithEquivalentRetryAsync()
    {
        await service.QueueHostContainerAsync(operationId, runner.Id, actorId, request, CancellationToken.None);
        await service.QueueHostContainerAsync(operationId, runner.Id, actorId,
            request with { Environment = request.Environment.ToDictionary(pair => pair.Key, pair => pair.Value) },
            CancellationToken.None);
    }

    public async Task<HostContainerExecutionOperationResponse?> CompleteAsLostAsync()
    {
        await service.ApplyAsync(runner.ApiKeyId!.Value, operationId,
            RunnerExecutionResult.Interrupted("Runner stopped."), CancellationToken.None, runnerId: runner.Id);
        return await service.ReadHostContainerOperationAsync(
            runner.ApiKeyId.Value, operationId, Guid.Empty, runner.Id, CancellationToken.None);
    }

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
