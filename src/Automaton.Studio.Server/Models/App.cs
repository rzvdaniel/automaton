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
    
    // Background customization for card display
    public string? BackgroundColor { get; set; } = "#ffffff";
    public string? BackgroundImageUrl { get; set; }
    public double BackgroundImageOpacity { get; set; } = 1.0;
    
    public AppDefinition Definition { get; set; } = new AppDefinition();
}
