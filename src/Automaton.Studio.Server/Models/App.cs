namespace Automaton.Studio.Server.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; } // Owner user ID (for file path resolution)
    public string Name { get; set; }
    public string Summary { get; set; }
    public string Description { get; set; }
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public Guid? ExecutionRunnerId { get; set; }

    // Project context
    public Enums.AccessScope Scope { get; set; }
    public Guid? ProjectId { get; set; }

    // Analytics
    public int ViewCount { get; set; }
    public int StarCount { get; set; }
    public DateTime? LastViewedAt { get; set; }

    // Sample/Featured flags
    public bool IsSample { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsProfileFeatured { get; set; }

    // Public profile display
    public string? ThumbnailUrl { get; set; }
    public List<AppScreenshotModel> Screenshots { get; set; } = new();
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public string MarketplaceStatus { get; set; } = Enums.MarketplaceAppStatuses.Approved;
    public string? MarketplaceModerationReason { get; set; }
    public DateTime? MarketplaceModeratedAt { get; set; }
    public Guid? MarketplaceModeratedByUserId { get; set; }

    public AppDefinition Definition { get; set; } = new AppDefinition();
}

public class AppScreenshotModel
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
