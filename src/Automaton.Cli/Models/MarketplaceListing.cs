namespace Automaton.Cli.Models;

/// <summary>Fields left null are not changed. An empty category clears it.</summary>
public sealed record MarketplaceListingUpdate(
    string? Summary = null,
    string? Description = null,
    string? Category = null,
    IReadOnlyList<string>? Tags = null)
{
    public bool IsEmpty => Summary is null && Description is null && Category is null && Tags is null;
}

public sealed record MarketplaceListing(
    string AppId,
    string Name,
    string Summary,
    string Description,
    string? Category,
    IReadOnlyList<string> Tags,
    string? CoverUrl,
    IReadOnlyList<MarketplaceScreenshot> Screenshots);

public sealed record MarketplaceScreenshot(string Id, string ImageUrl);
