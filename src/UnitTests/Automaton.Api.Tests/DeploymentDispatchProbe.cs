using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

namespace Automaton.Api.Tests;

internal sealed class DeploymentDispatchProbe(AppDeploymentTargetStateService targets, Guid targetId)
    : IHostedRunnerDeploymentCommandDispatcher, IEventPublisher
{
    public TaskCompletionSource HostPublished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RunnerExecutionRequestedEvent> ManagementPublished { get; }
        = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int HostDispatchCount { get; private set; }
    public Guid AttemptId { get; private set; }

    public async Task<bool> PublishAsync(DeploymentOperation operation, Guid executionAttemptId, CancellationToken cancellationToken)
    {
        await targets.MarkStartedAsync(operation.WebAppId, targetId, cancellationToken, operation.Id, executionAttemptId);
        HostDispatchCount++;
        AttemptId = executionAttemptId;
        HostPublished.TrySetResult();
        return true;
    }

    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        ManagementPublished.TrySetResult((RunnerExecutionRequestedEvent)(object)message);
        return Task.CompletedTask;
    }
}
