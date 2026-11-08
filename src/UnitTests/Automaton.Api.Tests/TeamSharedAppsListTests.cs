using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class TeamSharedAppsListTests
{
    [Test]
    public async Task AppsReachedThroughProjectMembershipAreListedOnlyForActiveMembers()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var (appId, projectId, ownerId, memberId, outsiderId) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        AppAuthorizationTestData.SeedProjectScopedApp(db, appId, projectId, ownerId, memberId);
        await db.SaveChangesAsync();
        var service = new OrganizationService(
            db, AppAuthorizationTestData.CreateUserContextService(memberId), null!,
            new OrganizationAiPolicySettingsService(db), null!);

        Assert.That((await service.GetMyAssignedAppsAsync(memberId)).Select(a => a.Id), Is.EqualTo(new[] { appId }));
        Assert.That(await service.GetMyAssignedAppsAsync(outsiderId), Is.Empty);

        db.ProjectMembers.Single(m => m.UserId == memberId).IsActive = false;
        await db.SaveChangesAsync();
        Assert.That(await service.GetMyAssignedAppsAsync(memberId), Is.Empty);
    }
}
