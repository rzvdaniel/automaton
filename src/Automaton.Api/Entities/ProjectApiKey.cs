namespace Automaton.Api.Entities;

/// <summary>
/// Links an API Key to a Project, enabling project-based access control.
/// </summary>
public class ProjectApiKey
{
    /// <summary>
    /// The project this API key belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The API key that is part of this project
    /// </summary>
    public Guid ApiKeyId { get; set; }
    
    /// <summary>
    /// When this API key was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this API key to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual ApiKey? ApiKey { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
