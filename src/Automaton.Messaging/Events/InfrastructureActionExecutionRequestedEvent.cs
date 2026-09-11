namespace Automaton.Messaging.Events;

public sealed class InfrastructureActionExecutionRequestedEvent
{
    public Guid OperationId { get; init; }
    public Guid HostedRunnerId { get; init; }
    public Guid UserId { get; init; }
    public Guid RunnerHostId { get; init; }
    public string ActionKey { get; init; } = string.Empty;
    public string InputJson { get; init; } = "{}";
    public IReadOnlyList<Guid> SecretGrantIds { get; init; } = Array.Empty<Guid>();
    public string CorrelationId { get; init; } = string.Empty;

    public InfrastructureCommandRequestedEvent ToInfrastructureCommand(Guid infrastructureRunnerId, DateTime createdAtUtc)
        => new(
            CommandId: OperationId,
            OperationId,
            RunnerHostId,
            infrastructureRunnerId,
            ActionKey,
            InputJson,
            SecretGrantIds.ToArray(),
            createdAtUtc);
}
