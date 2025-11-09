using Automaton.Core.Enums;

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
    
    // Access control
    public bool RequiresAuthentication { get; set; } = false; // Deprecated - use PublishingScope
    public PublishingScope PublishingScope { get; set; } = PublishingScope.Anonymous;
    public string? AllowedRoles { get; set; }
    
    // Project/Organization foreign key
    public Guid? ProjectId { get; set; }
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
    public virtual Project Project { get; set; }
}
