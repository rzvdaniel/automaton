namespace Automaton.Messaging.Events;

public sealed class ManagedRunnerProvisionRequestedEvent
{
    public Guid OperationId { get; init; }
    public Guid HostedRunnerId { get; init; }
    public Guid UserId { get; init; }
    public Guid RunnerHostId { get; init; }
    public IReadOnlyList<Guid> SecretGrantIds { get; init; } = Array.Empty<Guid>();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
