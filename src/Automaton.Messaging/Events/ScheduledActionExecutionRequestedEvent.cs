namespace Automaton.Messaging.Events;

public sealed class ScheduledActionExecutionRequestedEvent
{
    public Guid OccurrenceId { get; init; }
    public Guid ScheduleId { get; init; }
    public DateTime ScheduledForUtc { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}
