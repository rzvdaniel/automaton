using Automaton.Cli.Services;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class SetupServiceTests
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
    public async Task SetupAsync_WhenServerIsMissing_UsesHostedDefaultAndSavesLocalConfig()
    {
        var projectRoot = CreateProjectRoot();
        var service = new SetupService(
            new ConfigService(),
            (_, _, _) => Task.FromResult(ParseUser("550e8400-e29b-41d4-a716-446655440000", "Razvan", "razvan@example.com")));

        var result = await service.SetupAsync(projectRoot, null, "ak_test", global: false);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Server, Is.EqualTo(SetupService.DefaultHostedServer));
        Assert.That(result.UserName, Is.EqualTo("Razvan"));
        Assert.That(result.Checks.Any(check => check.Name == "aiGuidance" && check.Success), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".github", "skills", "automaton-app", "SKILL.md")), Is.True);

        var config = new ConfigService().LoadProjectLocalConfig(projectRoot);
        Assert.That(config.Server, Is.EqualTo(SetupService.DefaultHostedServer));
        Assert.That(config.ApiKey, Is.EqualTo("ak_test"));
    }

    [Test]
    public async Task SetupAsync_WhenApiKeyIsMissing_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new SetupService(new ConfigService(), (_, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.SetupAsync(projectRoot, null, null, global: false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Setup failed"));
        Assert.That(result.Checks.Any(check => check.Name == "credentials" && !check.Success), Is.True);
        Assert.That(result.NextSteps, Has.Some.EqualTo("Provide --key or answer the API key prompt"));
    }

    [Test]
    public async Task SetupAsync_WhenAuthenticationFails_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new SetupService(new ConfigService(), (_, _, _) => throw new InvalidOperationException("invalid api key"));

        var result = await service.SetupAsync(projectRoot, null, "ak_bad", global: false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "authentication" && !check.Success && check.Message.Contains("invalid api key")), Is.True);
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-setup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static JsonElement ParseUser(string id, string name, string email)
    {
        using var document = JsonDocument.Parse($$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "email": "{{email}}"
        }
        """);

        return document.RootElement.Clone();
    }
}