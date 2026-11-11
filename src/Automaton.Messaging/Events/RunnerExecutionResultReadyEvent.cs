namespace Automaton.Messaging.Events;

/// <summary>A host-container operation result is saved; tells its Runner to read it. The database row stays the source of truth.</summary>
public sealed record RunnerExecutionResultReadyEvent(Guid OperationId, Guid RunnerId);
