using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Entities;

public class AuditEvent
{
    public Guid Id { get; set; }

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? TargetUserId { get; set; }

    [MaxLength(256)]
    public string? UserName { get; set; }

    [MaxLength(32)]
    public string Outcome { get; set; } = "Success";

    [MaxLength(128)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    [MaxLength(128)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
