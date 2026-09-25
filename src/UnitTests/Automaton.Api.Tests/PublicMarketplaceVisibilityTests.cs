using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PublicMarketplaceVisibilityTests
{
    [Test]
    public async Task GetApps_PrivateMediaPreviewSuppressesRuntimeUrlAndLaunch()
    {
        await using var db = CreateDbContext();
        var app = CreatePrivateMediaPreview();
        db.WebApps.Add(app);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.GetApps();
        var response = result.Result as OkObjectResult;
        var apps = response?.Value as List<MarketplaceAppCardDto>;
        Assert.That(apps, Is.Not.Null);
        var card = apps.Single();

        Assert.Multiple(() =>
        {
            Assert.That(card.Id, Is.EqualTo(app.Id));
            Assert.That(card.CanLaunch, Is.False);
            Assert.That(card.PublicUrl, Is.Null);
        });
    }

    [Test]
    public async Task TrackAppLaunch_PrivateMediaPreviewReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var app = CreatePrivateMediaPreview();
        db.WebApps.Add(app);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.TrackAppLaunch(app.Id);

        Assert.That(result, Is.TypeOf<NotFoundObjectResult>());
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PublicMarketplaceController CreateController(ApplicationDbContext db)
        => new(db, null!, null!, new PublicMarketplaceCreatorAppsQuery(db), new PublicMarketplaceCatalogQuery(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static WebApp CreatePrivateMediaPreview() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Private preview",
        Summary = string.Empty,
        Description = string.Empty,
        Definition = "{}",
        Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow,
        IsPublished = true,
        PublicUrl = "private-preview",
        RuntimeVisibility = AppRuntimeVisibilities.Private,
        CatalogVisibility = AppCatalogVisibilities.Listed,
        MarketplacePreviewMode = MarketplacePreviewModes.MediaOnly,
        ReviewStatus = AppReviewStatuses.Approved,
        AccessStatus = AppAccessStatuses.Active
    };
}
