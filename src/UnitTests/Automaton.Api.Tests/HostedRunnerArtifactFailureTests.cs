using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class HostedRunnerArtifactFailureTests
{
    [TestCase("Lost")]
    [TestCase("Failed")]
    [TestCase("Cancelled")]
    public async Task TerminalArtifactResultFailsDeploymentAndCallbackReplayLeavesNewDeploymentAlone(string outcome)
    {
        await using var scenario = await ArtifactFailureScenario.CreateAsync(outcome);
        Assert.That(await scenario.CallbackAsync(), Is.EqualTo("accepted"));
        scenario.AssertFailedAndReleased();
        var snapshot = scenario.Operation.ResultJson;
        Assert.That(await scenario.CallbackAsync(), Is.EqualTo("already-applied"));
        Assert.That(scenario.Operation.ResultJson, Is.EqualTo(snapshot));
        var next = await scenario.StartNewDeploymentAsync();
        Assert.That(await scenario.CallbackAsync(), Is.EqualTo("already-applied"));
        Assert.That(scenario.State.Target.ActiveDeploymentOperationId, Is.EqualTo(next));
        Assert.That(scenario.State.Target.Status, Is.EqualTo(DeploymentTargetStatus.Deploying));
        Assert.That(await scenario.State.Db.RunnerExecutionOperations.CountAsync(), Is.EqualTo(1));
    }
}

internal sealed class ArtifactFailureScenario(DeploymentFixtureState state, DeploymentFixtureWorkspace workspace,
    DeploymentOperation operation, HostedRunnerDeploymentResultService results) : IAsyncDisposable
{
    public DeploymentFixtureState State => state;
    public DeploymentOperation Operation => operation;

    public static async Task<ArtifactFailureScenario> CreateAsync(string outcome)
    {
        var workspace = await DeploymentFixtureWorkspaceFactory.CreateAsync(false);
        var state = await DeploymentFixtureStateFactory.CreateAsync(workspace, new(false, false, false, false, null, null, null));
        state.Runner.ApiKeyId = Guid.NewGuid();
        var operation = await CreateOperationAsync(state, workspace);
        var operations = RunnerExecutionTestServices.Operations(state.Db);
        await SeedTerminalArtifactsAsync(state, operation, operations, outcome);
        return new(state, workspace, operation, ArtifactFailureResultServices.Create(state, workspace.RootPath, operations));
    }

    public async Task<string> CallbackAsync()
    {
        var receipt = await results.ApplyAsync(state.Runner.ApiKeyId!.Value, operation.Id,
            new(operation.ExecutionAttemptId!.Value,
                new() { DeploymentTargetId = state.Target.Id, Success = true, Message = "Host promotion completed." }), CancellationToken.None);
        await state.Db.Entry(operation).ReloadAsync();
        await state.Db.Entry(state.Target).ReloadAsync();
        return receipt;
    }

    public void AssertFailedAndReleased()
    {
        Assert.Multiple(() =>
        {
            Assert.That(operation.Status, Is.EqualTo(DeploymentOperationStatuses.Failed));
            Assert.That(operation.ErrorMessage, Is.EqualTo("Artifact replacement did not complete."));
            Assert.That(operation.CompletedAtUtc, Is.Not.Null);
            Assert.That(operation.LeaseExpiresAtUtc, Is.Null);
            Assert.That(state.Target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
            Assert.That(state.Target.ActiveDeploymentOperationId, Is.Null);
            Assert.That(state.Target.ActiveDeploymentExecutionAttemptId, Is.Null);
            Assert.That(operation.ResultJson, Does.Contain("\"success\":false"));
        });
    }

    public async Task<Guid> StartNewDeploymentAsync()
    {
        var id = Guid.NewGuid();
        await new AppDeploymentTargetStateService(state.Db).MarkStartedAsync(state.AppId, state.Target.Id,
            deploymentOperationId: id, executionAttemptId: Guid.NewGuid());
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await state.Db.DisposeAsync();
        Directory.Delete(workspace.RootPath, true);
    }

    private static async Task<DeploymentOperation> CreateOperationAsync(DeploymentFixtureState state, DeploymentFixtureWorkspace workspace)
    {
        _ = DeploymentPackageArchiveFactory.Create(state.AppId, workspace.PackagePath, true);
        var operation = await DeploymentOperationTestData.CreateAsync(state.Db, state.AppId, state.OwnerId,
            DeploymentOperationStatuses.Running, workspace.PackagePath, executionAttemptId: Guid.NewGuid(),
            leaseExpiresAtUtc: DateTime.UtcNow.AddMinutes(4));
        await new AppDeploymentTargetStateService(state.Db).MarkStartedAsync(state.AppId, state.Target.Id,
            deploymentOperationId: operation.Id, executionAttemptId: operation.ExecutionAttemptId);
        return operation;
    }

    private static async Task SeedTerminalArtifactsAsync(DeploymentFixtureState state, DeploymentOperation operation,
        RunnerExecutionOperationService operations, string outcome)
    {
        var id = ManagedApplicationArtifactGateway.OperationIdentity(operation.Id, state.Runner.Id);
        await operations.QueueManagedArtifactsAsync(id, state.Runner.Id, state.OwnerId,
            new(state.AppId, state.Target.Id, []), CancellationToken.None);
        await operations.ApplyAsync(state.Runner.ApiKeyId!.Value, id,
            new() { Outcome = outcome, Error = "Artifact replacement did not complete." }, CancellationToken.None);
    }

}
