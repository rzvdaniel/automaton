using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Services;

public static class AppVisibilityPolicy
{
    public static void EnsureValidConfiguration(
        string runtimeVisibility,
        string catalogVisibility,
        string marketplacePreviewMode)
    {
        if (runtimeVisibility is not (AppRuntimeVisibilities.Private or AppRuntimeVisibilities.Public) ||
            catalogVisibility is not (AppCatalogVisibilities.Unlisted or AppCatalogVisibilities.Listed) ||
            marketplacePreviewMode is not (MarketplacePreviewModes.Launchable or MarketplacePreviewModes.MediaOnly))
        {
            throw new InvalidOperationException("Invalid app visibility configuration.");
        }

        if (catalogVisibility == AppCatalogVisibilities.Listed &&
            runtimeVisibility == AppRuntimeVisibilities.Private &&
            marketplacePreviewMode != MarketplacePreviewModes.MediaOnly)
        {
            throw new InvalidOperationException(
                "A listed App with a private runtime must use the media-only marketplace preview.");
        }
    }

    public static bool IsMarketplaceVisible(WebApp app)
    {
        return app.CatalogVisibility == AppCatalogVisibilities.Listed &&
               app.ReviewStatus == AppReviewStatuses.Approved &&
               app.AccessStatus == AppAccessStatuses.Active;
    }

    public static bool IsPubliclyAccessible(WebApp app)
    {
        return app.RuntimeVisibility == AppRuntimeVisibilities.Public &&
               app.AccessStatus == AppAccessStatuses.Active;
    }

    public static bool IsLaunchableMarketplacePreview(WebApp app) =>
        IsMarketplaceVisible(app) &&
        app.MarketplacePreviewMode == MarketplacePreviewModes.Launchable &&
        IsPubliclyAccessible(app);
}
