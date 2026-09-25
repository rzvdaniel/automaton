namespace Automaton.Api.Models;

public class MarketplaceAppCardDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CreatorPublicUsername { get; set; }
    public string? CreatorDisplayName { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public int ViewCount { get; set; }
    public int StarCount { get; set; }
    public int ForkCount { get; set; }
    public bool IsForkable { get; set; }
    public bool IsStarredByCurrentUser { get; set; }
    public string? PublicUrl { get; set; }
    public bool CanLaunch { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool HasApplicationSource { get; set; }
    public bool IsFeatured { get; set; }
    public List<MarketplaceAppScreenshotDto> Screenshots { get; set; } = new();
}

public class MarketplaceAppDetailsDto : MarketplaceAppCardDto
{
    public bool CanRemix { get; set; }
    public List<MarketplaceAppCardDto> RelatedApps { get; set; } = new();
    public List<MarketplaceAppCardDto> ForksAndRemixes { get; set; } = new();
}

public class MarketplaceAppScreenshotDto
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
