using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

public class WebApp
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; }
    
    // Publishing fields
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; } = 1;

    public Guid? ExecutionRunnerId { get; set; }
    
    // Access control (future use)
    public bool RequiresAuthentication { get; set; } = false;
    public string? AllowedRoles { get; set; }
    
    // Project/Organization access control
    /// <summary>
    /// The access scope for this app (Personal, Project, or Public)
    /// </summary>
    public AccessScope Scope { get; set; } = AccessScope.Personal;
    
    /// <summary>
    /// If Scope is Project, this is the project the app belongs to
    /// </summary>
    public Guid? ProjectId { get; set; }

    // Appearance settings stored as JSON
    public string Appearance { get; set; } = "{}";
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    // Navigation properties
    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
    public virtual ICollection<ProjectApp> ProjectApps { get; set; }
}
