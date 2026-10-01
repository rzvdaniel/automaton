namespace Automaton.Messaging.Events;

// Only identities enter the broker/outbox; decrypted script inputs travel over authenticated HTTPS.
public sealed record RunnerExecutionRequestedEvent(Guid OperationId, Guid RunnerId);
