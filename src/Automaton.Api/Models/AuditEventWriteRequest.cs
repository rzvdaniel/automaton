namespace Automaton.Api.Models;

public class AuditEventWriteRequest
{
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Message { get; set; } = string.Empty;

    public object? Metadata { get; set; }

    public string Outcome { get; set; } = "Success";

    public string? EventType { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? TargetUserId { get; set; }

    public bool FailOnError { get; set; }
}
