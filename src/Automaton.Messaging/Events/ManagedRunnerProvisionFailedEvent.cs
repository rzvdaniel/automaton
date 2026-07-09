namespace Automaton.Messaging.Events;

public sealed class ManagedRunnerProvisionFailedEvent
{
    public Guid HostedRunnerId { get; init; }
    public Guid UserId { get; init; }
    public string Error { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
