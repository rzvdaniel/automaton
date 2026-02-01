using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Represents a project/organization workspace that contains apps, flows, runners, and custom steps.
/// </summary>
public class Project
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// URL-friendly unique name (e.g., "engineering-tools")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name shown in UI (e.g., "Engineering Tools")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional description of the project
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// The user who owns/created this project
    /// </summary>
    public Guid OwnerId { get; set; }
    
    /// <summary>
    /// Navigation property to the owner
    /// </summary>
    public virtual ApplicationUser? Owner { get; set; }
    
    /// <summary>
    /// Optional organization this project belongs to. Null for personal projects.
    /// </summary>
    public Guid? OrganizationId { get; set; }
    
    /// <summary>
    /// Navigation property to the organization (if project is org-scoped)
    /// </summary>
    public virtual Organization? Organization { get; set; }
    
    /// <summary>
    /// When the project was created
    /// </summary>
    public DateTime Created { get; set; }
    
    /// <summary>
    /// When the project was last updated
    /// </summary>
    public DateTime Updated { get; set; }
    
    /// <summary>
    /// Soft delete flag
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public virtual ICollection<ProjectApp> ProjectApps { get; set; } = new List<ProjectApp>();
    public virtual ICollection<ProjectFlow> ProjectFlows { get; set; } = new List<ProjectFlow>();
    public virtual ICollection<ProjectRunner> ProjectRunners { get; set; } = new List<ProjectRunner>();
    public virtual ICollection<ProjectCustomStep> ProjectCustomSteps { get; set; } = new List<ProjectCustomStep>();
    public virtual ICollection<ProjectApiKey> ProjectApiKeys { get; set; } = new List<ProjectApiKey>();
    public virtual ICollection<ProjectAiApiKey> ProjectAiApiKeys { get; set; } = new List<ProjectAiApiKey>();
}
