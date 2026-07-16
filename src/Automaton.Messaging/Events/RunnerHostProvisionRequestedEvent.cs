namespace Automaton.Messaging.Events;

public sealed class RunnerHostProvisionRequestedEvent
{
    public Guid RunnerHostId { get; init; }
    public Guid ProvisioningOperationId { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
