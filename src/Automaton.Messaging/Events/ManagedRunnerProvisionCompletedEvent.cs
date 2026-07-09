namespace Automaton.Messaging.Events;

public sealed class ManagedRunnerProvisionCompletedEvent
{
    public Guid HostedRunnerId { get; init; }
    public Guid UserId { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
