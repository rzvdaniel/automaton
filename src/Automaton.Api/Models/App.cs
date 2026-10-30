using Automaton.Api.Enums;
using Automaton.Core.Models;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models;

public class AppModel
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; } // Owner user ID (for file path resolution)
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [JsonRequired]
    public bool IsPublished { get; set; }
    [JsonRequired]
    public bool IsForkable { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; }
    [JsonRequired]
    public bool RequiresAuthentication { get; set; } = true;
    public string RuntimeVisibility { get; set; } = AppRuntimeVisibilities.Private;
    public string CatalogVisibility { get; set; } = AppCatalogVisibilities.Unlisted;
    public string MarketplacePreviewMode { get; set; } = MarketplacePreviewModes.Launchable;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public Guid? ExecutionRunnerId { get; set; }

    // Project context
    public AccessScope Scope { get; set; }
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
    public string MarketplaceStatus { get; set; } = MarketplaceAppStatuses.Approved;
    public string ReviewStatus { get; set; } = AppReviewStatuses.Approved;
    public string AccessStatus { get; set; } = AppAccessStatuses.Active;
    public string? MarketplaceModerationReason { get; set; }
    public DateTime? MarketplaceModeratedAt { get; set; }
    public Guid? MarketplaceModeratedByUserId { get; set; }
    public Guid? ForkedFromAppId { get; set; }
    public Guid? ForkedFromAuthorUserId { get; set; }
    public string? ForkedFromAppName { get; set; }
    public string? ForkedFromAuthorName { get; set; }
    public string DevelopmentMode { get; set; } = Constants.AppDevelopmentModes.Studio;
    public bool SourceAvailable { get; set; } = true;
    public string RuntimeSource { get; set; } = Constants.AppRuntimeSources.Studio;

    public AppDefinition Definition { get; set; } = new AppDefinition();
    // The full app representation includes the canonical contract, but update
    // payloads may omit it when changing unrelated metadata. The service then
    // preserves the persisted contract instead of replacing it with defaults.
    public ApplicationContract? Application { get; set; }
}

public class ForkAppRequest
{
    public string? Name { get; set; }
}

public class ForkAppSourceModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? CreatorPublicUsername { get; set; }
    public string CreatorDisplayName { get; set; } = "Automaton creator";
    public bool HasApplicationSource { get; set; }
    public ApplicationContract? Application { get; set; }
    public string RuntimeLabel => HasApplicationSource
        ? "Application runtime is defined by its contract"
        : "No application source";
}

public class AppScreenshotModel
{
    public Guid Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}
