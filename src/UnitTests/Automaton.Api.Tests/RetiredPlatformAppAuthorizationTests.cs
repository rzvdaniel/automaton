using Automaton.Api.Services;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RetiredPlatformAppAuthorizationTests
{
    [Test]
    public async Task CanDeleteAppAsync_RejectsRetiredPlatformAppEvenForRecordedOwner()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, InfrastructureAppTrust.PlatformAppId, userId, "retired-platform");
        var ordinaryAppId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, ordinaryAppId, userId, "ordinary");

        var authorization = new ResourceAuthorizationService(db, null!);
        var canDeletePlatformApp = await authorization.CanDeleteAppAsync(InfrastructureAppTrust.PlatformAppId, userId);
        var canDeleteOrdinaryApp = await authorization.CanDeleteAppAsync(ordinaryAppId, userId);

        Assert.Multiple(() =>
        {
            Assert.That(canDeletePlatformApp, Is.False);
            Assert.That(canDeleteOrdinaryApp, Is.True);
        });
    }
}
