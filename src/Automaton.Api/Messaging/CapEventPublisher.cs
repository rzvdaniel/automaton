using Automaton.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class CapEventPublisher(ICapPublisher capPublisher) : IEventPublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        return message switch
        {
            RunnerExecutionRequestedEvent => capPublisher.PublishAsync(EventTopics.RunnerExecutionRequested, message, cancellationToken: cancellationToken),
            UserRegisteredEvent => capPublisher.PublishAsync(EventTopics.UserRegistered, message, cancellationToken: cancellationToken),
            DeploymentPackageRequestedEvent => capPublisher.PublishAsync(EventTopics.DeploymentPackageRequested, message, cancellationToken: cancellationToken),
            _ => throw new InvalidOperationException($"No topic configured for event type '{typeof(T).FullName}'.")
        };
    }
}
