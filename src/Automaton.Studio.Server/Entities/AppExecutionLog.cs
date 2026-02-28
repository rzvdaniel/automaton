namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Logs a single controller action execution triggered from a published web app.
/// </summary>
public class AppExecutionLog
{
    public Guid Id { get; set; }

    /// <summary>The app that triggered the execution.</summary>
    public Guid AppId { get; set; }

    public string ControllerName { get; set; } = string.Empty;

    public string ActionName { get; set; } = string.Empty;

    /// <summary>JSON-serialised request inputs.</summary>
    public string? RequestPayload { get; set; }

    /// <summary>JSON-serialised response outputs.</summary>
    public string? ResponsePayload { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Wall-clock duration of the execution in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Authenticated user who called the endpoint, if any.</summary>
    public Guid? UserId { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }
}
