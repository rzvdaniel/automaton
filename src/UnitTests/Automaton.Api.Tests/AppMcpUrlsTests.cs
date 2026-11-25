using Automaton.Api.Config;
using Automaton.Api.Mcp;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMcpUrlsTests
{
    private static readonly PublishedAppsConfig Config = new() { PublicBaseUrl = "https://apps.example.test" };

    [Test]
    public void PublicApp_GetsItsAddressNameOnTheAppsDomain()
        => Assert.That(AppMcpUrls.For(Config, Guid.NewGuid(), "hello", true), Is.EqualTo("https://hello.apps.example.test/"));

    [Test]
    public void PrivateApp_GetsItsPrivateHost_EvenWithAnAddressName()
    {
        var id = Guid.NewGuid();
        Assert.That(AppMcpUrls.For(Config, id, "hello", false), Is.EqualTo($"https://app-{id:N}.apps.example.test/"));
        Assert.That(AppMcpUrls.For(Config, id, null, true), Is.EqualTo($"https://app-{id:N}.apps.example.test/"));
    }

    [Test]
    public void NoAppsDomainConfigured_GivesNoUrl()
        => Assert.That(AppMcpUrls.For(null, Guid.NewGuid(), "hello", true), Is.Null);

    [Test]
    public void NonDefaultPort_IsKept()
        => Assert.That(AppMcpUrls.For(new() { PublicBaseUrl = "http://apps.localhost:5173" }, Guid.NewGuid(), "x", true), Is.EqualTo("http://x.apps.localhost:5173/"));
}
