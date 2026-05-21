using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class NewAppServiceTests
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
    public async Task CreateAsync_WhenNameAndCredentialsArePresent_CreatesAppAndWritesProjectFiles()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(
            new ConfigService(),
            (_, _, name, _) => Task.FromResult(((string)"app-123", name)));

        var result = await service.CreateAsync(projectRoot, "My App", "https://automaton.monster", "ak_test");

        Assert.That(result.Success, Is.True);
        Assert.That(result.AppId, Is.EqualTo("app-123"));
        Assert.That(result.Name, Is.EqualTo("My App"));
        Assert.That(result.Checks.Any(check => check.Name == "aiGuidance" && check.Success), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "automaton.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automatonignore")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "instructions.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms.txt")), Is.True);

        var config = new ConfigService().LoadProjectConfig(projectRoot);
        Assert.That(config.AppId, Is.EqualTo("app-123"));
        Assert.That(config.Name, Is.EqualTo("My App"));
        Assert.That(config.Server, Is.EqualTo("https://automaton.monster"));
        Assert.That(config.Frontend?.Build, Is.EqualTo("cd frontend && npm install && npm run build"));
        Assert.That(config.Frontend?.DeployDir, Is.EqualTo("frontend/dist"));
        Assert.That(config.Backend?.DeployDir, Is.EqualTo("backend"));
    }

    [Test]
    public async Task CreateAsync_WhenCredentialsAreMissing_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.CreateAsync(projectRoot, "My App", null, null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "credentials" && !check.Success), Is.True);
    }

    [Test]
    public async Task CreateAsync_WhenNameIsMissing_ReturnsFailureBeforeApiCall()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.CreateAsync(projectRoot, null, "https://automaton.monster", "ak_test");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "createApp" && !check.Success && check.Message.Contains("app name is required")), Is.True);
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-newapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
