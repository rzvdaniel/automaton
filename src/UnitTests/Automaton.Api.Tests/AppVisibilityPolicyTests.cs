using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppVisibilityPolicyTests
{
    [Test]
    public void PrivateListedMediaOnlyApp_IsVisibleButNotLaunchable()
    {
        var app = new WebApp
        {
            CatalogVisibility = AppCatalogVisibilities.Listed,
            RuntimeVisibility = AppRuntimeVisibilities.Private,
            MarketplacePreviewMode = MarketplacePreviewModes.MediaOnly,
            ReviewStatus = AppReviewStatuses.Approved,
            AccessStatus = AppAccessStatuses.Active
        };

        Assert.Multiple(() =>
        {
            Assert.That(AppVisibilityPolicy.IsMarketplaceVisible(app), Is.True);
            Assert.That(AppVisibilityPolicy.IsPubliclyAccessible(app), Is.False);
            Assert.That(AppVisibilityPolicy.IsLaunchableMarketplacePreview(app), Is.False);
        });
    }

    [Test]
    public void PublicUnlistedApp_IsNotDiscoverable()
    {
        var app = new WebApp
        {
            CatalogVisibility = AppCatalogVisibilities.Unlisted,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            MarketplacePreviewMode = MarketplacePreviewModes.Launchable,
            ReviewStatus = AppReviewStatuses.Approved,
            AccessStatus = AppAccessStatuses.Active
        };

        Assert.That(AppVisibilityPolicy.IsMarketplaceVisible(app), Is.False);
    }

    [Test]
    public void ListedPrivateLaunchableConfiguration_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppVisibilityPolicy.EnsureValidConfiguration(
                AppRuntimeVisibilities.Private,
                AppCatalogVisibilities.Listed,
                MarketplacePreviewModes.Launchable));

        Assert.That(exception!.Message, Does.Contain("media-only"));
    }
}
