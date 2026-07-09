using Automaton.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Worker.Consumers;

public sealed class ManagedRunnerProvisionRequestedCapSubscriber(
    IEventHandler<ManagedRunnerProvisionRequestedEvent> eventHandler) : ICapSubscribe
{
    [CapSubscribe(EventTopics.ManagedRunnerProvisionRequested, Group = "automaton.worker")]
    public Task HandleAsync(
        ManagedRunnerProvisionRequestedEvent message,
        CancellationToken cancellationToken)
    {
        return eventHandler.HandleAsync(message, cancellationToken);
    }
}
