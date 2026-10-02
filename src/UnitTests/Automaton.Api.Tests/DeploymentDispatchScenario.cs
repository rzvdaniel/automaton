using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal sealed class DeploymentDispatchScenario : IAsyncDisposable
{
    private readonly ApplicationDeploymentServiceTests.DeploymentFixture fixture;
    private readonly ApplicationDbContext callbackDb;
    private readonly DeploymentDispatchProbe probe;
    private readonly RunnerExecutionOperationService operations;
    private readonly HostedRunnerDeploymentResultService results;
    private readonly DeploymentDispatchConsumer consumer;
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(15));
    private Task<string>? pendingCallback;

    private DeploymentDispatchScenario(ApplicationDeploymentServiceTests.DeploymentFixture fixture,
        ApplicationDbContext callbackDb, DeploymentOperation operation, DeploymentDispatchProbe probe)
    {
        this.fixture = fixture;
        this.callbackDb = callbackDb;
        Operation = operation;
        this.probe = probe;
        operations = RunnerExecutionTestServices.Operations(callbackDb, probe);
        var state = new DeploymentFixtureState(callbackDb, fixture.OwnerId, fixture.AppId, fixture.ApiIdentity,
            Target.ExecutionRunner!, Target);
        results = ArtifactFailureResultServices.Create(state, fixture.RootPath, operations);
        consumer = new(fixture, probe, CompleteManagementAsync, lifetime.Token);
    }

    public DeploymentOperation Operation { get; }
    public AppDeploymentTarget Target => fixture.Target;
    public int HostDispatchCount => probe.HostDispatchCount;
    public bool PackageExists => File.Exists(fixture.PackagePath);

    public static async Task<DeploymentDispatchScenario> CreateAsync()
    {
        var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        _ = fixture.CreatePackage(hasRuntime: false);
        fixture.Target.ExecutionRunner!.ApiKeyId = Guid.NewGuid();
        var operation = await DeploymentOperationTestData.CreateAsync(fixture.Db, fixture.AppId, fixture.OwnerId,
            DeploymentOperationStatuses.Pending, fixture.PackagePath);
        return new(fixture, DeploymentDispatchServices.CreateCallbackContext(fixture.Db), operation,
            new DeploymentDispatchProbe(new AppDeploymentTargetStateService(fixture.Db), fixture.Target.Id));
    }

    public async Task StartDispatcherAsync()
    {
        await consumer.StartAsync();
        await probe.HostPublished.Task.WaitAsync(lifetime.Token);
    }

    public Task StopDispatcherAsync() => consumer.StopAsync();

    public Task<string> ReportPromotionAsync() => pendingCallback = results.ApplyAsync(Target.ExecutionRunner!.ApiKeyId!.Value, Operation.Id,
        new(probe.AttemptId, new() { DeploymentTargetId = Target.Id, Success = true, Message = "Host promoted." }), lifetime.Token);

    public Task DeliverDeploymentAsync() => consumer.DeliverDeploymentAsync(Operation.Id);

    public async Task DeliverManagementAsync()
    {
        var command = await probe.ManagementPublished.Task.WaitAsync(lifetime.Token);
        Assert.That(command.Management, Is.True);
        await operations.CancelAsync(command.OperationId, lifetime.Token);
        await consumer.DeliverManagementAsync(command);
    }

    public async Task ReloadAsync()
    {
        await fixture.Db.Entry(Operation).ReloadAsync(lifetime.Token);
        await fixture.Db.Entry(Target).ReloadAsync(lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await consumer.StopAsync();
        if (pendingCallback is not null)
        {
            try { await pendingCallback; }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                // Close the result callback's package stream before deleting the test workspace.
            }
        }
        lifetime.Dispose();
        await consumer.DisposeAsync();
        await callbackDb.DisposeAsync();
        await fixture.DisposeAsync();
    }

    private async Task CompleteManagementAsync(RunnerExecutionRequestedEvent command)
    {
        var key = Target.ExecutionRunner!.ApiKeyId!.Value;
        var assignment = await operations.AssignmentAsync(key, command.OperationId, lifetime.Token);
        Assert.That(assignment.Disposition, Is.EqualTo("cancel"));
        Assert.That(assignment.Request!.ManagedArtifacts!.DeploymentTargetId, Is.EqualTo(Target.Id));
        // The retained live child has cancellation intent after its callback timed out.
        await operations.ApplyAsync(key, command.OperationId,
            new RunnerExecutionResult { Outcome = "Cancelled", Error = "Artifact synchronization cancelled." }, lifetime.Token);
    }

}
