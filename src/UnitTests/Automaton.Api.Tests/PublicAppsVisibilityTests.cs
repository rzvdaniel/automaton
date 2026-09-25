namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PublicAppsVisibilityTests
{
    [Test]
    public async Task CreatorApps_ListsMediaPreviewButSuppressesItsRuntimeUrl()
    {
        await using var fixture = await PublicAppsTestFixture.CreateAsync();

        var profile = await fixture.Service.GetPublicProfileAsync("creator");
        var apps = await fixture.Service.GetPublicAppsAsync("creator");

        Assert.Multiple(() =>
        {
            Assert.That(profile!.PublicAppCount, Is.EqualTo(1));
            Assert.That(apps, Has.Count.EqualTo(1));
            Assert.That(apps[0].CanLaunch, Is.False);
            Assert.That(apps[0].PublicUrl, Is.Null);
            Assert.That(apps[0].AppUrl, Is.Empty);
        });
    }

}
