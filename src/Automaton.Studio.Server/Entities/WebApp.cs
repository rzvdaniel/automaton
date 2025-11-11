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

    public string? BackgroundColor { get; set; } = "#ffffff";
    public string? BackgroundImageUrl { get; set; }
    public double BackgroundImageOpacity { get; set; } = 1.0;
    public string? TitleFontColor { get; set; } = "#ffffff";
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
}
