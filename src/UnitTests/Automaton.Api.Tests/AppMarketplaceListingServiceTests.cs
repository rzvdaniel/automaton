using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMarketplaceListingServiceTests
{
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid DeveloperId = Guid.NewGuid();
    private static readonly Guid ViewerId = Guid.NewGuid();

    [Test]
    public async Task UpdateAsync_ChangesOnlyTheFieldsSent()
    {
        using var db = CreateSeededDb();
        var app = db.WebApps.Single(item => item.Id == AppId);
        app.Description = "Keep me";
        db.SaveChanges();

        var result = await CreateService(db, DeveloperId).UpdateAsync(AppId, new UpdateAppListingRequest(Summary: "  A word game  "));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Updated));
            Assert.That(app.Summary, Is.EqualTo("A word game"));
            Assert.That(app.Description, Is.EqualTo("Keep me"));
        });
    }

    [Test]
    public async Task UpdateAsync_StoresTheCanonicalCategoryNameAndNormalizedTags()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, DeveloperId).UpdateAsync(
            AppId,
            new UpdateAppListingRequest(Category: "games", Tags: ["game", " Words ", "game"]));

        var app = db.WebApps.Single(item => item.Id == AppId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Updated));
            Assert.That(app.Category, Is.EqualTo("Games"));
            Assert.That(app.Tags, Is.EqualTo("game,Words"));
        });
    }

    [Test]
    public async Task UpdateAsync_EmptyCategoryClearsIt()
    {
        using var db = CreateSeededDb();
        db.WebApps.Single(item => item.Id == AppId).Category = "Games";
        db.SaveChanges();

        await CreateService(db, DeveloperId).UpdateAsync(AppId, new UpdateAppListingRequest(Category: ""));

        Assert.That(db.WebApps.Single(item => item.Id == AppId).Category, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_RejectsUnknownCategoryAndListsTheAvailableOnes()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, DeveloperId).UpdateAsync(AppId, new UpdateAppListingRequest(Category: "Sports"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Invalid));
            Assert.That(result.Error, Does.Contain("Games"));
        });
    }

    [Test]
    public async Task UpdateAsync_RejectsTooLongDescription()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, DeveloperId).UpdateAsync(
            AppId,
            new UpdateAppListingRequest(Description: new string('a', AppMarketplaceListingService.MaximumDescriptionLength + 1)));

        Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Invalid));
    }

    [Test]
    public async Task UpdateAsync_ProjectViewerCanSeeButNotChangeTheListing()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, ViewerId).UpdateAsync(AppId, new UpdateAppListingRequest(Summary: "Changed"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Forbidden));
            Assert.That(db.WebApps.Single(item => item.Id == AppId).Summary, Is.Empty);
        });
    }

    [Test]
    public async Task UpdateAsync_UnrelatedUserGetsNotFound()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, Guid.NewGuid()).UpdateAsync(AppId, new UpdateAppListingRequest(Summary: "Changed"));

        Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.NotFound));
    }

    [Test]
    public async Task ImageChanges_RequireUpdateAccessNotJustVisibility()
    {
        using var db = CreateSeededDb();

        var viewer = await CreateImageService(db, ViewerId).UploadScreenshotAsync(AppId, null, "viewer");
        var developer = await CreateImageService(db, DeveloperId).UploadScreenshotAsync(AppId, null, "developer");

        Assert.Multiple(() =>
        {
            Assert.That(viewer.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
            // The developer passes the permission check and reaches file validation.
            Assert.That(developer.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        });
    }

    [TestCase(null, true)]
    [TestCase("Developer", true)]
    [TestCase("FullAccess", true)]
    [TestCase("ReadOnly", false)]
    [TestCase("Runner", false)]
    public void AppChangeKeyScope_AllowsSignedInUsersAndWritableKeys(string? scope, bool allowed)
    {
        var claims = scope is null ? Array.Empty<Claim>() : [new Claim("api_key_scope", scope)];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        Assert.That(AppChangeKeyScope.IsAllowed(user), Is.EqualTo(allowed));
    }

    private static AppMarketplaceListingService CreateService(ApplicationDbContext db, Guid userId)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppMarketplaceListingService(
            db,
            new ResourceAuthorizationService(db, userContext),
            new RecentActivityService(db, userContext));
    }

    private static AppImageManagementService CreateImageService(ApplicationDbContext db, Guid userId)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppImageManagementService(
            AppAuthorizationTestServiceFactory.CreateAppsService(db, userId),
            new ResourceAuthorizationService(db, userContext),
            new AppAuthorizationTestWebHostEnvironment(Path.GetTempPath()),
            NullLogger<AppImageManagementService>.Instance);
    }

    private static ApplicationDbContext CreateSeededDb()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedProjectScopedApp(db, AppId, ProjectId, OwnerId, DeveloperId);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = ProjectId,
            UserId = ViewerId,
            Role = ProjectRole.Viewer,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        });
        db.MarketplaceCategories.AddRange(
            new MarketplaceCategory { Id = Guid.NewGuid(), Name = "Education", Slug = "education", SortOrder = 1 },
            new MarketplaceCategory { Id = Guid.NewGuid(), Name = "Games", Slug = "games", SortOrder = 2 });
        db.SaveChanges();
        return db;
    }
}
