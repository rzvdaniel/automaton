using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Worker.Consumers;

public sealed class RunnerHostProvisionRequestedCapSubscriber(
    RunnerHostProvisionRequestedEventHandler handler) : ICapSubscribe
{
    [CapSubscribe(EventTopics.RunnerHostProvisionRequested, Group = "automaton.worker")]
    public Task HandleAsync(RunnerHostProvisionRequestedEvent message, CancellationToken cancellationToken)
        => handler.HandleAsync(message, cancellationToken);
}
