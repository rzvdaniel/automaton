namespace Automaton.Messaging.Events;

public sealed record UserRegisteredEvent(
    Guid UserId,
    string Email,
    string? DisplayName,
    DateTime RegisteredAtUtc,
    string? CorrelationId = null);
