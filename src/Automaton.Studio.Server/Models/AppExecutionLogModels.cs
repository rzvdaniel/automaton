#nullable disable

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to log a controller execution.
/// </summary>
public class AppExecutionLogRequest
{
    public Guid AppId { get; set; }
    public string ControllerName { get; set; }
    public string ActionName { get; set; }
    public string RequestPayload { get; set; }
    public string ResponsePayload { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public Guid? UserId { get; set; }
    public string IpAddress { get; set; }
}

/// <summary>
/// DTO returned to the frontend for a single execution log entry.
/// </summary>
public class AppExecutionLogDto
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public string ControllerName { get; set; }
    public string ActionName { get; set; }
    public string RequestPayload { get; set; }
    public string ResponsePayload { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public Guid? UserId { get; set; }
    public string IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Query parameters for fetching execution logs.
/// </summary>
public class AppExecutionLogQuery
{
    public bool? Success { get; set; }
    public string ControllerName { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Generic paged result wrapper.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// DTO for admin Serilog log entries.
/// </summary>
public class AdminLogEntryDto
{
    public string Id { get; set; }
    public string Message { get; set; }
    public string Level { get; set; }
    public DateTime? Timestamp { get; set; }
    public string Exception { get; set; }
    public string Properties { get; set; }
    public string UserName { get; set; }
}

/// <summary>
/// Query parameters for admin log viewer.
/// </summary>
public class AdminLogQuery
{
    public string Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
