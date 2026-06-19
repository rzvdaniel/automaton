namespace Automaton.Api.Entities;

/// <summary>
/// Links a WebApp to a Project, enabling project-based access control.
/// </summary>
public class ProjectApp
{
    /// <summary>
    /// The project this app belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The app that is part of this project
    /// </summary>
    public Guid WebAppId { get; set; }
    
    /// <summary>
    /// When this app was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this app to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual WebApp? WebApp { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
