namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links a Flow to a Project, enabling project-based access control.
/// </summary>
public class ProjectFlow
{
    /// <summary>
    /// The project this flow belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The flow that is part of this project
    /// </summary>
    public Guid FlowId { get; set; }
    
    /// <summary>
    /// When this flow was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this flow to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual Flow? Flow { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
