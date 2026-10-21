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
            ScheduledActionExecutionRequestedEvent => capPublisher.PublishAsync(EventTopics.ScheduledActionExecutionRequested, message, cancellationToken: cancellationToken),
            UserRegisteredEvent => capPublisher.PublishAsync(EventTopics.UserRegistered, message, cancellationToken: cancellationToken),
            InfrastructureCommandRequestedEvent => capPublisher.PublishAsync(EventTopics.InfrastructureCommandRequested, message, cancellationToken: cancellationToken),
            _ => throw new InvalidOperationException($"No topic configured for event type '{typeof(T).FullName}'.")
        };
    }
}
