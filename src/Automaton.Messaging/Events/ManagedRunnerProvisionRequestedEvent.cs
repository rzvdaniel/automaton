namespace Automaton.Messaging.Events;

public sealed class ManagedRunnerProvisionRequestedEvent
{
    public Guid HostedRunnerId { get; init; }
    public Guid UserId { get; init; }
    public Guid SubscriptionPlanId { get; init; }
    public Guid RunnerHostId { get; init; }
    public string RunnerApiKey { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
