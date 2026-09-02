using Automaton.Api.Services;
using Automaton.Messaging;
using Automaton.Messaging.Events;
using DotNetCore.CAP;

namespace Automaton.Api.Messaging;

public sealed class DeploymentPackageRequestedCapSubscriber(
    DeploymentOperationService operationService) : ICapSubscribe
{
    [CapSubscribe(EventTopics.DeploymentPackageRequested, Group = "automaton.api")]
    public Task HandleAsync(DeploymentPackageRequestedEvent message, CancellationToken cancellationToken)
        => operationService.ExecuteAsync(message.OperationId, cancellationToken);
}
