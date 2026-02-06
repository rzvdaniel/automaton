namespace Automaton.Studio.Server.Models;

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
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Guid? AiSessionId { get; set; }
    public string? AiModel { get; set; }
}

/// <summary>
/// Detailed version information including file and step changes.
/// </summary>
public class AppVersionDetailDto
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public int VersionNumber { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public Guid? AiSessionId { get; set; }
    public string? AiModel { get; set; }
    public string SnapshotPath { get; set; } = string.Empty;
    
    /// <summary>
    /// JSON structure describing file changes: { "addedFiles": [], "modifiedFiles": [], "deletedFiles": [] }
    /// </summary>
    public string? FilesChangeSummary { get; set; }
    
    /// <summary>
    /// Steps that were captured in this version.
    /// </summary>
    public List<VersionStepChangeDto> StepChanges { get; set; } = new();
}

/// <summary>
/// Represents a change to a custom step as captured in a version.
/// </summary>
public class VersionStepChangeDto
{
    public Guid Id { get; set; }
    public Guid CustomStepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // AppVersionStepAction enum name: Created, Modified, Deleted, Linked, Unlinked
    public string? Language { get; set; } // e.g., "Python", "PowerShell"
    
    /// <summary>
    /// Full code snapshot of the step at version time.
    /// </summary>
    public string? CodeSnapshot { get; set; }
    
    /// <summary>
    /// JSON structure with input and output variables at version time.
    /// </summary>
    public string? VariablesSnapshot { get; set; }
}

/// <summary>
/// Request to manually create a version snapshot (alternative to automatic session-based creation).
/// </summary>
public class CreateVersionSnapshotRequest
{
    public string? Trigger { get; set; } = "ManualSave"; // Default to ManualSave if not specified
}

/// <summary>
/// Request to restore an app from a previous version.
/// </summary>
public class RestoreVersionRequest
{
    public int VersionNumber { get; set; }
}

/// <summary>
/// Response after restoring a version.
/// </summary>
public class RestoreVersionResponse
{
    public Guid AppId { get; set; }
    public int RestoredFromVersion { get; set; }
    public DateTime RestoredAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Paginated list of versions.
/// </summary>
public class AppVersionListDto
{
    public Guid AppId { get; set; }
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public List<AppVersionDto> Versions { get; set; } = new();
}
