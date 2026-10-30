using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;

namespace Automaton.Api.Tests;

// A project app with a Developer and a Viewer, plus marketplace categories (one inactive).
internal static class AppMarketplaceListingTestData
{
    public static readonly Guid AppId = Guid.NewGuid();
    public static readonly Guid ProjectId = Guid.NewGuid();
    public static readonly Guid OwnerId = Guid.NewGuid();
    public static readonly Guid DeveloperId = Guid.NewGuid();
    public static readonly Guid ViewerId = Guid.NewGuid();

    public static ApplicationDbContext CreateSeededDb()
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
            new MarketplaceCategory { Id = Guid.NewGuid(), Name = "Games", Slug = "games", SortOrder = 2 },
            new MarketplaceCategory { Id = Guid.NewGuid(), Name = "Retired category", Slug = "retired", SortOrder = 3, IsActive = false });
        db.SaveChanges();
        return db;
    }
}
