using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class MachineStaticNginxConfigurationTests
{
    private static readonly Guid SpaApp = Guid.NewGuid();
    private static readonly Guid PlainApp = Guid.NewGuid();
    private static readonly Dictionary<Guid, Guid> Sites = new() { [SpaApp] = Guid.NewGuid(), [PlainApp] = Guid.NewGuid() };

    [Test]
    public void StaticSiteAnswers404ForMissingFilesUnlessItIsASinglePageApp()
    {
        var config = MachineStaticSitePublisher.BuildNginxConfiguration(Sites, 8080, new HashSet<Guid> { SpaApp });

        var spa = Block(config, SpaApp);
        var plain = Block(config, PlainApp);
        Assert.That(spa, Does.Contain($"/{SpaApp:D}/{Sites[SpaApp]:D}/index.html;"));
        Assert.That(plain, Does.Contain("try_files $uri $uri/ =404;"));
        Assert.That(plain, Does.Not.Contain($"/{PlainApp:D}/{Sites[PlainApp]:D}/index.html;"));
    }

    [Test]
    public void WithoutASinglePageAppSetNoSiteFallsBackToTheIndexPage()
        => Assert.That(MachineStaticSitePublisher.BuildNginxConfiguration(Sites, 8080), Does.Not.Contain("/index.html;"));

    private static string Block(string config, Guid app)
    {
        var start = config.IndexOf($"location ^~ /{app:D}/", StringComparison.Ordinal);
        return config[start..config.IndexOf('}', start)];
    }
}
