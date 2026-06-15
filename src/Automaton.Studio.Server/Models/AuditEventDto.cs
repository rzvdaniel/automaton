namespace Automaton.Studio.Server.Models;

public class AuditEventDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? EntityDisplay { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? UserName { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AuditEventQuery
{
    public string? EventType { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? Outcome { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
