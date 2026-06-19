using Automaton.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Worker.Consumers;

public sealed class UserRegisteredCapSubscriber(
    IEventHandler<UserRegisteredEvent> eventHandler) : ICapSubscribe
{
    [CapSubscribe(EventTopics.UserRegistered, Group = "automaton.worker")]
    public Task HandleAsync(
        UserRegisteredEvent message,
        CancellationToken cancellationToken)
    {
        return eventHandler.HandleAsync(message, cancellationToken);
    }
}
