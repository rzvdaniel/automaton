namespace Automaton.Studio.Server.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public Guid? ExecutionRunnerId { get; set; }

    // Analytics
    public int ViewCount { get; set; }
    public DateTime? LastViewedAt { get; set; }

    // Sample/Featured flags
    public bool IsSample { get; set; }
    public bool IsFeatured { get; set; }
    
    // Appearance configuration stored as JSON
    public AppAppearance Appearance { get; set; } = new AppAppearance();
    
    public AppDefinition Definition { get; set; } = new AppDefinition();
}
