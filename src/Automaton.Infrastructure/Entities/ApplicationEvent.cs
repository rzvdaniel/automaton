using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class ApplicationEvent
{
    public Guid Id { get; set; }

    public Guid AppId { get; set; }

    [MaxLength(32)]
    public string Level { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(128)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
