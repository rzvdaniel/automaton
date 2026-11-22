using Automaton.Api.Data;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class AppPublicationRestartTests
{
    private sealed class CountingRestarter : IAppRuntimeRestarter
    {
        public int Restarts;
        public Task<int> RestartRunningAsync(Guid appId, CancellationToken token, bool wait = false) { Restarts++; return Task.FromResult(1); }
    }

    [TestCase("""{"settings":[{"name":"URL","default":"{{app.address}}"}]}""", 1)]
    [TestCase("""{"settings":[{"name":"URL","default":"x"}]}""", 0)]
    public async Task FirstPublishRestartsOnlyAnAppWhoseSettingFollowsItsAddress(string contract, int expected)
    {
        var (service, restarter, appId) = Create(contract);

        await service.Publish(appId, "blog", requiresAuthentication: false);

        Assert.That(restarter.Restarts, Is.EqualTo(expected));
    }

    [Test]
    public async Task PublishingAgainWithTheSameAddressDoesNotRestart()
    {
        var (service, restarter, appId) = Create("""{"settings":[{"name":"URL","default":"{{app.address}}"}]}""");
        await service.Publish(appId, "blog", requiresAuthentication: false);

        await service.Publish(appId, "blog", requiresAuthentication: false);

        Assert.That(restarter.Restarts, Is.EqualTo(1));
    }

    private static (AppPublicationService Service, CountingRestarter Restarter, Guid AppId) Create(string contract)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "unpublished");
        var app = db.WebApps.Single(item => item.Id == appId);
        app.PublicUrl = null;
        app.ApplicationContractJson = contract;
        db.SaveChanges();
        var restarter = new CountingRestarter();
        return (AppAuthorizationTestServiceFactory.CreatePublicationService(db, ownerId, restarter), restarter, appId);
    }
}
