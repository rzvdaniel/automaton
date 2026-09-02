namespace Automaton.Messaging.Events;

public sealed class DeploymentPackageRequestedEvent
{
    public Guid OperationId { get; init; }
}
