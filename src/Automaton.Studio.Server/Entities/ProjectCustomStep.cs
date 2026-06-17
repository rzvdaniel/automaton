namespace Automaton.Api.Entities;

/// <summary>
/// Links a CustomStep to a Project, enabling project-based access control.
/// </summary>
public class ProjectCustomStep
{
    /// <summary>
    /// The project this custom step belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The custom step that is part of this project
    /// </summary>
    public Guid CustomStepId { get; set; }
    
    /// <summary>
    /// When this custom step was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this custom step to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual CustomStep? CustomStep { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
