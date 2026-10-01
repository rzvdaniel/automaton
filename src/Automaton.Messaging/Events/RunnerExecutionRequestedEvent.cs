namespace Automaton.Messaging.Events;

// The outbox carries stable identities and queue selection; executable inputs travel over authenticated HTTPS.
public sealed record RunnerExecutionRequestedEvent(Guid OperationId, Guid RunnerId, bool Management = false);
