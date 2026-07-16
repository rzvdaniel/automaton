using Automaton.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Worker.Messaging;

public sealed class CapEventPublisher(ICapPublisher capPublisher) : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        return message switch
        {
            UserRegisteredEvent => capPublisher.PublishAsync(EventTopics.UserRegistered, message, cancellationToken: cancellationToken),
            ManagedRunnerProvisionRequestedEvent => capPublisher.PublishAsync(EventTopics.ManagedRunnerProvisionRequested, message, cancellationToken: cancellationToken),
            ManagedRunnerProvisionCompletedEvent => capPublisher.PublishAsync(EventTopics.ManagedRunnerProvisionCompleted, message, cancellationToken: cancellationToken),
            ManagedRunnerProvisionFailedEvent => capPublisher.PublishAsync(EventTopics.ManagedRunnerProvisionFailed, message, cancellationToken: cancellationToken),
            RunnerHostProvisionRequestedEvent => capPublisher.PublishAsync(EventTopics.RunnerHostProvisionRequested, message, cancellationToken: cancellationToken),
            _ => throw new InvalidOperationException($"No topic configured for event type '{typeof(T).FullName}'.")
        };
    }
}
