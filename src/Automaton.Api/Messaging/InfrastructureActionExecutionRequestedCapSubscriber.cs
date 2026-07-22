using Automaton.Messaging;
using Automaton.Messaging.Events;
using Automaton.Api.Services;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class InfrastructureActionExecutionRequestedCapSubscriber(
    InfrastructureActionProvisioningHandler handler) : ICapSubscribe
{
    [CapSubscribe(EventTopics.InfrastructureActionExecutionRequested, Group = "automaton.api")]
    public Task HandleAsync(InfrastructureActionExecutionRequestedEvent message, CancellationToken cancellationToken)
        => handler.HandleAsync(message, cancellationToken);
}
