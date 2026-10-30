using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Automaton.Api.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Models;

public sealed class UpdateAppRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Summary { get; set; } = string.Empty;
    [Required]
    public string Description { get; set; } = string.Empty;
    [JsonRequired]
    public bool IsPublished { get; set; }
    [JsonRequired]
    public bool IsForkable { get; set; }
    public string? PublicUrl { get; set; }
    [JsonRequired]
    public bool RequiresAuthentication { get; set; }
    public string RuntimeVisibility { get; set; } = AppRuntimeVisibilities.Private;
    public string CatalogVisibility { get; set; } = AppCatalogVisibilities.Unlisted;
    public string MarketplacePreviewMode { get; set; } = MarketplacePreviewModes.Launchable;
    public Guid? ExecutionRunnerId { get; set; }
    [JsonRequired]
    public bool IsProfileFeatured { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    [JsonRequired]
    public AppDefinition Definition { get; set; } = new();
    public ApplicationContract? Application { get; set; }

    public AppModel ToAppModel() => new()
    {
        Name = Name,
        Summary = Summary,
        Description = Description,
        IsPublished = IsPublished,
        IsForkable = IsForkable,
        PublicUrl = PublicUrl,
        RequiresAuthentication = RequiresAuthentication,
        RuntimeVisibility = RuntimeVisibility,
        CatalogVisibility = CatalogVisibility,
        MarketplacePreviewMode = MarketplacePreviewMode,
        ExecutionRunnerId = ExecutionRunnerId,
        IsProfileFeatured = IsProfileFeatured,
        ThumbnailUrl = ThumbnailUrl,
        Category = Category,
        Tags = Tags,
        Definition = Definition,
        Application = Application
    };
}
