using Automaton.Cli.Services;
using Automaton.AiGuidance;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class LinkAppServiceTests
{
    private readonly List<string> _tempDirectories = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        _tempDirectories.Clear();
    }

    [Test]
    public async Task LinkAsync_WhenAppIdAndCredentialsArePresent_WritesProjectFiles()
    {
        var projectRoot = CreateProjectRoot();
        var references = GetReferenceSources();
        var service = new LinkAppService(
            new ConfigService(),
            (_, _, _, _) => Task.FromResult(ParseApp("app-456", "Linked App")),
            (_, _, _) => Task.FromResult(references));

        var result = await service.LinkAsync(projectRoot, "app-456", "https://automaton.monster", "ak_test");

        Assert.That(result.Success, Is.True);
        Assert.That(result.AppId, Is.EqualTo("app-456"));
        Assert.That(result.Name, Is.EqualTo("Linked App"));
        Assert.That(result.Checks.Any(check => check.Name == "aiGuidance" && check.Success), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "automaton.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automatonignore")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".github")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms")), Is.True);
    }

    [Test]
    public async Task LinkAsync_WhenAppIdIsMissing_ReturnsFailureBeforeApiCall()
    {
        var projectRoot = CreateProjectRoot();
        var service = new LinkAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.LinkAsync(projectRoot, null, "https://automaton.monster", "ak_test");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "linkApp" && !check.Success && check.Message.Contains("app ID is required")), Is.True);
    }

    [Test]
    public async Task LinkAsync_WhenCredentialsAreMissing_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new LinkAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.LinkAsync(projectRoot, "app-456", null, null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "credentials" && !check.Success), Is.True);
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-linkapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static IReadOnlyList<GuidanceSource> GetReferenceSources() =>
        BuildExportSources();

    private static IReadOnlyList<GuidanceSource> BuildExportSources()
    {
        var provider = new EmbeddedAutomatonGuidanceProvider();
        var seededSources = provider.GetSeedSources();
        var sources = new List<GuidanceSource>();

        foreach (var source in seededSources)
        {
            var metadata = AutomatonGuidanceSeedCatalog.Resolve(source.RelativePath);
            if (!string.IsNullOrWhiteSpace(metadata.OutputPath))
            {
                sources.Add(new GuidanceSource(metadata.OutputPath!, source.Content));
            }
        }

        return sources;
    }

    private static JsonElement ParseApp(string id, string name)
    {
        using var document = JsonDocument.Parse($$"""
        {
          "id": "{{id}}",
          "name": "{{name}}"
        }
        """);

        return document.RootElement.Clone();
    }
}
