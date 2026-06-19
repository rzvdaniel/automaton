namespace Automaton.Api.Entities;

/// <summary>
/// Links an AI API Key to a Project, enabling project-based access control.
/// </summary>
public class ProjectAiApiKey
{
    /// <summary>
    /// The project this AI API key belongs to
    /// </summary>
    public Guid ProjectId { get; set; }
    
    /// <summary>
    /// The AI API key that is part of this project
    /// </summary>
    public Guid AiApiKeyId { get; set; }
    
    /// <summary>
    /// When this AI API key was added to the project
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Who added this AI API key to the project
    /// </summary>
    public Guid? AddedBy { get; set; }
    
    // Navigation properties
    public virtual Project? Project { get; set; }
    public virtual AiApiKey? AiApiKey { get; set; }
    public virtual ApplicationUser? AddedByUser { get; set; }
}
