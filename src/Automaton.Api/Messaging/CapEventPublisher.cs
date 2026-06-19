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

        return typeof(T) == typeof(UserRegisteredEvent)
            ? capPublisher.PublishAsync(EventTopics.UserRegistered, message, cancellationToken: cancellationToken)
            : throw new InvalidOperationException($"No topic configured for event type '{typeof(T).FullName}'.");
    }
}
