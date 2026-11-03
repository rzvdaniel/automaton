using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using static Automaton.Api.Tests.AppMarketplaceListingTestData;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionHintTests
{
    [TestCase("stranger")]
    [TestCase("viewer")]
    public void ADeleteByAnyoneWhoIsNotTheOwner_IsRefusedWithoutRevealingProjects(string caller)
    {
        using var db = CreateSeededDb();
        var userId = caller == "viewer" ? ViewerId : Guid.NewGuid();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, userId);

        var error = Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.Remove(AppId));

        Assert.That(error!.Message, Does.Not.Contain("project"));
    }

    [Test]
    public void TheOwnerOfAProjectApp_StillLearnsWhyTheDeleteIsBlocked()
    {
        using var db = CreateSeededDb();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(db, DeveloperId);

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.Remove(AppId));

        Assert.That(error!.Message, Does.Contain("part of one or more projects"));
    }
}
