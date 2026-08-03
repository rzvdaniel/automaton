using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PublicAppsVisibilityTests
{
    [Test]
    public async Task CreatorApps_ListsMediaPreviewButSuppressesItsRuntimeUrl()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "creator", PublicUsername = "creator" };
        var listed = CreateApp("media-preview", AppCatalogVisibilities.Listed);
        var unlisted = CreateApp("hidden", AppCatalogVisibilities.Unlisted);
        db.Users.Add(user);
        db.WebApps.AddRange(listed, unlisted);
        db.WebAppUsers.AddRange(
            new WebAppUser { WebAppId = listed.Id, UserId = user.Id },
            new WebAppUser { WebAppId = unlisted.Id, UserId = user.Id });
        await db.SaveChangesAsync();
        var service = new PublicAppsService(
            db,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.example.test" }),
            NullLogger<PublicAppsService>.Instance);

        var profile = await service.GetPublicProfileAsync("creator");
        var apps = await service.GetPublicAppsAsync("creator");

        Assert.Multiple(() =>
        {
            Assert.That(profile!.PublicAppCount, Is.EqualTo(1));
            Assert.That(apps, Has.Count.EqualTo(1));
            Assert.That(apps[0].CanLaunch, Is.False);
            Assert.That(apps[0].PublicUrl, Is.Null);
            Assert.That(apps[0].AppUrl, Is.Empty);
        });
    }

    private static WebApp CreateApp(string slug, string catalogVisibility) => new()
    {
        Id = Guid.NewGuid(), Name = slug, Summary = string.Empty, Description = string.Empty,
        Definition = "{}", Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
        IsPublished = true, PublicUrl = slug, CatalogVisibility = catalogVisibility,
        RuntimeVisibility = AppRuntimeVisibilities.Private,
        MarketplacePreviewMode = MarketplacePreviewModes.MediaOnly,
        ReviewStatus = AppReviewStatuses.Approved, AccessStatus = AppAccessStatuses.Active
    };
}
