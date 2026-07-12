using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class CliAppUrlBuilderTests
{
    private string? _originalAppsBaseUrl;
    private string? _originalStudioBaseUrl;

    [SetUp]
    public void SetUp()
    {
        _originalAppsBaseUrl = Environment.GetEnvironmentVariable("AUTOMATON_APPS_BASE_URL");
        _originalStudioBaseUrl = Environment.GetEnvironmentVariable("AUTOMATON_STUDIO_BASE_URL");

        Environment.SetEnvironmentVariable("AUTOMATON_APPS_BASE_URL", null);
        Environment.SetEnvironmentVariable("AUTOMATON_STUDIO_BASE_URL", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_APPS_BASE_URL", _originalAppsBaseUrl);
        Environment.SetEnvironmentVariable("AUTOMATON_STUDIO_BASE_URL", _originalStudioBaseUrl);
    }

    [Test]
    public void BuildStudioAppUrl_WhenServerIsLocalhostApi_UsesStudioDevServerPort()
    {
        var url = CliAppUrlBuilder.BuildStudioAppUrl("http://localhost:5091", "app-123");

        Assert.That(url, Is.EqualTo("http://localhost:5173/apps/app-123"));
    }

    [Test]
    public void BuildStudioAppUrl_WhenServerIsLoopbackApi_UsesStudioDevServerPort()
    {
        var url = CliAppUrlBuilder.BuildStudioAppUrl("http://127.0.0.1:5091", "app-123");

        Assert.That(url, Is.EqualTo("http://127.0.0.1:5173/apps/app-123"));
    }

    [Test]
    public void BuildRestrictedAppUrl_WhenServerIsLocalhostApi_KeepsApiHost()
    {
        var url = CliAppUrlBuilder.BuildRestrictedAppUrl("http://localhost:5091", "app-123");

        Assert.That(url, Is.EqualTo("http://app-app123.apps.localhost:5091/"));
    }

    [Test]
    public void BuildStudioAppUrl_WhenStudioBaseUrlOverrideExists_PrefersEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_STUDIO_BASE_URL", "http://localhost:6001/");

        var url = CliAppUrlBuilder.BuildStudioAppUrl("http://localhost:5091", "app-123");

        Assert.That(url, Is.EqualTo("http://localhost:6001/apps/app-123"));
    }

    [Test]
    public void BuildStudioAppUrl_WhenServerIsProductionApi_RewritesToStudioSubdomain()
    {
        var url = CliAppUrlBuilder.BuildStudioAppUrl("https://api.automaton.monster", "app-123");

        Assert.That(url, Is.EqualTo("https://studio.automaton.monster/apps/app-123"));
    }
}
