using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class ScheduledActionExecutionRequestedCapSubscriber(
    ScheduledActionExecutionHandler handler) : ICapSubscribe
{
    // Dispatch awaits Runner management results. Keep its consumer separate from
    // the Runner execution publisher that must deliver those management commands.
    [CapSubscribe(EventTopics.ScheduledActionExecutionRequested, Group = "automaton.api.scheduled-actions")]
    public Task HandleAsync(ScheduledActionExecutionRequestedEvent message, CancellationToken cancellationToken)
        => handler.HandleAsync(message, cancellationToken);
}
