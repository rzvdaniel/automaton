using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class ScheduledActionExecutionRequestedCapSubscriber(
    ScheduledActionExecutionHandler handler) : ICapSubscribe
{
    [CapSubscribe(EventTopics.ScheduledActionExecutionRequested, Group = "automaton.api")]
    public Task HandleAsync(ScheduledActionExecutionRequestedEvent message, CancellationToken cancellationToken)
        => handler.HandleAsync(message, cancellationToken);
}
