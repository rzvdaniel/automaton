#nullable disable

using Automaton;

namespace Automaton.Api.Models;

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
    /// <summary>
    /// Normalized runtime events derived from the execution result.
    /// This is the forward-compatible contract for backend runtime output.
    /// The current implementation derives events from the stored controller
    /// response so Studio can evolve toward a language-agnostic console
    /// without requiring a storage migration first.
    /// </summary>
    public List<AppRuntimeLogEventDto> RuntimeEvents { get; set; } = [];
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public Guid? UserId { get; set; }
    public string IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AppRuntimeLogEventDto
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = AppRuntimeLogLevels.Info;
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = AppRuntimeLogSources.Backend;
    public string Stream { get; set; } = AppRuntimeLogStreams.Stdout;
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

public static class AppRuntimeLogLevels
{
    public const string Info = "info";
    public const string Error = "error";
}

public static class AppRuntimeLogSources
{
    public const string Backend = "backend";
}

public static class AppRuntimeLogStreams
{
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";
}
