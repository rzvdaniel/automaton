namespace Automaton.Api.Models;

/// <summary>
/// Represents a snapshot of an app at a point in time.
/// </summary>
public class AppVersionDto
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public int VersionNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty; // AppVersionTrigger enum name
    public string? Source { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Guid? AiSessionId { get; set; }
    public string? AiModel { get; set; }
    public bool SnapshotCreated { get; set; } = true;
}

/// <summary>
/// Request to manually create a version snapshot (alternative to automatic session-based creation).
/// </summary>
public class CreateVersionSnapshotRequest
{
    public string? Trigger { get; set; } = "ManualSave"; // Default to ManualSave if not specified
    public string? Source { get; set; }
    public string? Reason { get; set; }
    
    /// <summary>
    /// Optional user-provided summary for the snapshot. When provided, overrides the auto-generated summary.
    /// </summary>
    public string? Summary { get; set; }
}

public sealed class CreateVersionSnapshotResponse
{
    public bool SnapshotCreated { get; set; }
    public string Message { get; set; } = string.Empty;
    public AppVersionDto? Version { get; set; }
}

public static class AppVersionSources
{
    public const string AiChat = "AiChat";
    public const string CliDeploy = "CliDeploy";
    public const string Studio = "Studio";
}

public static class AppVersionReasons
{
    public const string AfterAiSession = "After AI Session";
    public const string BeforeDeployment = "Before Deployment";
    public const string Manual = "Manual";
    public const string Publish = "Publish";
    public const string Restore = "Restore";
}
