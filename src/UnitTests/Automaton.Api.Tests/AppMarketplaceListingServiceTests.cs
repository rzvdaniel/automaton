using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using NUnit.Framework;
using static Automaton.Api.Tests.AppMarketplaceListingTestData;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMarketplaceListingServiceTests
{
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
    public async Task UpdateAsync_RejectsInactiveCategoriesWithoutListingThem()
    {
        using var db = CreateSeededDb();

        var result = await CreateService(db, DeveloperId).UpdateAsync(AppId, new UpdateAppListingRequest(Category: "Retired"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(AppListingUpdateStatus.Invalid));
            Assert.That(result.Error, Does.Not.Contain("Retired category"));
        });
    }

    [Test]
    public async Task UpdateAsync_WritesAnAuditEventOnlyWhenSomethingChanges()
    {
        using var db = CreateSeededDb();
        var audit = new RecordingAudit();
        var service = CreateService(db, DeveloperId, audit);

        await service.UpdateAsync(AppId, new UpdateAppListingRequest(Summary: "A word game"));
        await service.UpdateAsync(AppId, new UpdateAppListingRequest(Summary: "A word game"));

        Assert.That(audit.EventTypes, Is.EqualTo(new[] { AuditEventTypes.AppMetadataUpdated }));
    }

    private static AppMarketplaceListingService CreateService(ApplicationDbContext db, Guid userId, RecordingAudit? audit = null)
    {
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        return new AppMarketplaceListingService(
            db,
            new ResourceAuthorizationService(db, userContext),
            userContext,
            new RecentActivityService(db, userContext),
            audit ?? new RecordingAudit());
    }

    private sealed class RecordingAudit : IAuditEventService
    {
        public List<string?> EventTypes { get; } = [];

        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            EventTypes.Add(request.EventType);
            return Task.CompletedTask;
        }

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
