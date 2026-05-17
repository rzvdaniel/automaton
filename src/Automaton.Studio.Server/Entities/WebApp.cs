using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Entities;

public class WebApp
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Summary { get; set; }
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
    
    // Project/Organization access control
    /// <summary>
    /// The access scope for this app (Personal, Project, or Public)
    /// </summary>
    public AccessScope Scope { get; set; } = AccessScope.Personal;
    
    /// <summary>
    /// If Scope is Project, this is the project the app belongs to
    /// </summary>
    public Guid? ProjectId { get; set; }

    // Analytics
    public int ViewCount { get; set; } = 0;
    public int StarCount { get; set; } = 0;
    public DateTime? LastViewedAt { get; set; }

    // Sample/Featured flags (admin-managed)
    public bool IsSample { get; set; } = false;
    public bool IsFeatured { get; set; } = false;

    // Creator-managed profile display
    public bool IsProfileFeatured { get; set; } = false;

    // Public profile display
    /// <summary>
    /// URL to app thumbnail image for card display
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// App category for organization (e.g., "Business", "Productivity", "Entertainment")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Comma-separated tags for search/filtering
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Marketplace moderation status controlling whether the app is publicly discoverable.
    /// </summary>
    public string MarketplaceStatus { get; set; } = Enums.MarketplaceAppStatuses.Approved;

    /// <summary>
    /// Optional admin moderation reason shown in the admin review experience.
    /// </summary>
    public string? MarketplaceModerationReason { get; set; }

    public DateTime? MarketplaceModeratedAt { get; set; }
    public Guid? MarketplaceModeratedByUserId { get; set; }

    /// <summary>
    /// Total tokens used in AI chat sessions for this app
    /// </summary>
    public int TotalChatTokensUsed { get; set; } = 0;

    /// <summary>
    /// Total number of prompts submitted in AI chat sessions for this app
    /// </summary>
    public int TotalPromptsSubmittedInChat { get; set; } = 0;

    /// <summary>
    /// Last time activity occurred in any chat session for this app
    /// </summary>
    public DateTime? LastChatActivityAt { get; set; }

    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    // Navigation properties
    public virtual ICollection<WebAppUser> WebAppUsers { get; set; }
    public virtual ICollection<WebAppScreenshot> WebAppScreenshots { get; set; } = new List<WebAppScreenshot>();
    public virtual ICollection<ProjectApp> ProjectApps { get; set; }
}
