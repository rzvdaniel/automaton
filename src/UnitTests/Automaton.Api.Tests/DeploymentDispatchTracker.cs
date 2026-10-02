using System.Collections.Concurrent;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal sealed class DeploymentDispatchTracker
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DeploymentDispatchObservation>> published = new();
    private readonly ConcurrentDictionary<Guid, int> counts = new();

    public Task<DeploymentDispatchObservation> Publication(Guid operationId)
        => published.GetOrAdd(operationId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public int Count(Guid operationId) => counts.GetValueOrDefault(operationId);

    public void Record(Guid operationId, Guid runnerId, Guid attemptId, ApplicationDbContext context)
    {
        counts.AddOrUpdate(operationId, 1, (_, count) => count + 1);
        published.GetOrAdd(operationId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult(new(runnerId, attemptId, context));
    }
}

internal sealed record DeploymentDispatchObservation(Guid RunnerId, Guid AttemptId, ApplicationDbContext Context);

internal sealed class ConcurrentDeploymentCommandProbe(ApplicationDbContext db, DeploymentDispatchTracker tracker)
    : IHostedRunnerDeploymentCommandDispatcher
{
    public async Task<bool> PublishAsync(DeploymentOperation operation, Guid executionAttemptId, CancellationToken cancellationToken)
    {
        var target = await db.AppDeploymentTargets.SingleAsync(item => item.WebAppId == operation.WebAppId && item.IsDefault,
            cancellationToken);
        await new AppDeploymentTargetStateService(db).MarkStartedAsync(operation.WebAppId, target.Id, cancellationToken,
            operation.Id, executionAttemptId);
        tracker.Record(operation.Id, target.ExecutionRunnerId!.Value, executionAttemptId, db);
        return true;
    }
}
