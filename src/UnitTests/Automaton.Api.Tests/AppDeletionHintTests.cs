using NUnit.Framework;
using static Automaton.Api.Tests.AppMarketplaceListingTestData;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionHintTests
{
    private const string NotAllowed = "You do not have permission to delete app";

    [TestCase("stranger")]
    [TestCase("project viewer")]
    [TestCase("project owner without a link to the app")]
    public void ADeleteByAnyoneWhoIsNotTheAppsUser_IsRefusedWithoutRevealingProjects(string caller)
    {
        using var db = CreateSeededDb();
        var userId = caller switch { "project viewer" => ViewerId, "project owner without a link to the app" => OwnerId, _ => Guid.NewGuid() };
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, userId);

        var error = Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Remove(AppId));

        Assert.That(error!.Message, Does.StartWith(NotAllowed).And.Not.Contain("project"));
    }

    [Test]
    public void AnUnknownAppLooksTheSameAsSomeoneElsesApp()
    {
        using var db = CreateSeededDb();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, Guid.NewGuid());

        var unknown = Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Remove(Guid.NewGuid()));

        Assert.That(unknown!.Message, Does.StartWith(NotAllowed));
    }

    [Test]
    public void TheAppsOwnUser_StillLearnsWhyTheDeleteIsBlocked()
    {
        using var db = CreateSeededDb();
        // DeveloperId is the user linked to the seeded app (its personal owner).
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, DeveloperId);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.Remove(AppId));

        Assert.That(error!.Message, Does.Contain("part of one or more projects"));
    }
}
