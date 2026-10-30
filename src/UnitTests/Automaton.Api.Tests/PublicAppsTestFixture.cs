using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal sealed class PublicAppsTestFixture : IAsyncDisposable
{
    public ApplicationDbContext Db { get; }
    public PublicAppsService Service { get; }

    private PublicAppsTestFixture(ApplicationDbContext db, PublicAppsService service)
    {
        Db = db;
        Service = service;
    }

    public static async Task<PublicAppsTestFixture> CreateAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
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
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.example.test" }));
        return new PublicAppsTestFixture(db, service);
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();

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
