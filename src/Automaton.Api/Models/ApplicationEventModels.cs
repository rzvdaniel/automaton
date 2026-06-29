#nullable disable

namespace Automaton.Api.Models;

public class ApplicationEventWriteRequest
{
    public Guid AppId { get; set; }
    public string Level { get; set; }
    public string Category { get; set; }
    public string EventType { get; set; }
    public string Message { get; set; }
    public object Metadata { get; set; }
    public Guid? ActorUserId { get; set; }
}

public class ApplicationEventDto
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string Level { get; set; }
    public string Category { get; set; }
    public string EventType { get; set; }
    public string Message { get; set; }
    public string MetadataJson { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ApplicationEventQuery
{
    public string Category { get; set; }
    public string Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
