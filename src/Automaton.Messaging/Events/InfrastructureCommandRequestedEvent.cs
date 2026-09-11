namespace Automaton.Messaging.Events;

public sealed record InfrastructureCommandRequestedEvent(
    Guid CommandId,
    Guid OperationId,
    Guid RunnerHostId,
    Guid InfrastructureRunnerId,
    string ActionKey,
    string InputJson,
    IReadOnlyList<Guid> SecretGrantIds,
    DateTime CreatedAtUtc);
