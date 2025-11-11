namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links a Runner to a Project, enabling project-based access control.
/// </summary>
public class ProjectRunner
{
    /// <summary>
    /// The project this runner belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The runner that is part of this project
    /// </summary>
    public Guid RunnerId { get; set; }
    
    /// <summary>
    /// When this runner was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this runner to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual Runner? Runner { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
