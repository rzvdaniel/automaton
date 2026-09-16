using Automaton.Cli.Services;
using Automaton.Cli.Models;
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
    public async Task LinkAsync_WhenLocalContractIsValid_WritesOnlyLinkageMetadata()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new Automaton.Cli.Models.AutomatonConfig
        {
            Name = "Local project",
            Application = ApplicationContractTestFactory.Create()
        });
        var configPath = Path.Combine(projectRoot, "automaton.json");
        var contractBefore = await File.ReadAllTextAsync(configPath);
        var sourcePath = Path.Combine(projectRoot, "src", "main.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "local source");
        var guidancePath = Path.Combine(projectRoot, "AGENTS.md");
        await File.WriteAllTextAsync(guidancePath, "user guidance");
        var service = new LinkAppService(
            configService,
            (_, _, _, _) => Task.FromResult(ParseApp("app-456", "Linked App")));

        var result = await service.LinkAsync(projectRoot, "app-456", "https://automaton.monster", "ak_test", "local");

        Assert.That(result.Success, Is.True);
        Assert.That(result.AppId, Is.EqualTo("app-456"));
        Assert.That(result.Name, Is.EqualTo("Linked App"));
        Assert.That(result.NextSteps, Has.Some.EqualTo("Run automaton deploy to deploy this local source"));
        Assert.That(File.ReadAllText(configPath), Is.EqualTo(contractBefore));
        Assert.That(File.ReadAllText(sourcePath), Is.EqualTo("local source"));
        Assert.That(File.ReadAllText(guidancePath), Is.EqualTo("user guidance"));
        var env = configService.LoadNamedEnvironmentConfig(projectRoot, "local");
        Assert.That(env!.AppId, Is.EqualTo("app-456"));
        Assert.That(env.ApiKey, Is.Null);
        Assert.That(configService.LoadProjectConfig(projectRoot).Name, Is.EqualTo("Local project"));
    }

    [Test]
    public async Task LinkAsync_WhenAppIdIsMissing_ReturnsFailureBeforeApiCall()
    {
        var projectRoot = CreateProjectRoot();
        var service = new LinkAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.LinkAsync(projectRoot, null, "https://automaton.monster", "ak_test", "local");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "linkApp" && !check.Success && check.Message.Contains("app ID is required")), Is.True);
    }

    [Test]
    public async Task LinkAsync_WhenEnvironmentIsOmitted_WritesLiveEnvironmentByDefault()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new Automaton.Cli.Models.AutomatonConfig
        {
            Name = "Local project",
            Application = ApplicationContractTestFactory.Create()
        });
        var service = new LinkAppService(
            configService,
            (_, _, _, _) => Task.FromResult(ParseApp("app-live", "Linked App")));

        var result = await service.LinkAsync(projectRoot, "app-live", "https://api.automaton.monster", "ak_live");

        Assert.That(result.Success, Is.True);

        var live = configService.LoadNamedEnvironmentConfig(projectRoot, ConfigService.DefaultEnvironmentName);
        var selection = configService.LoadProjectEnvironmentSelection(projectRoot);

        Assert.That(live, Is.Not.Null);
        Assert.That(live!.AppId, Is.EqualTo("app-live"));
        Assert.That(selection.Default, Is.EqualTo(ConfigService.DefaultEnvironmentName));
    }

    [Test]
    public async Task LinkAsync_WhenCredentialsAreMissing_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new LinkAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.LinkAsync(projectRoot, "app-456", null, null, "local");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "credentials" && !check.Success), Is.True);
    }

    [Test]
    public async Task LinkAsync_WhenAutomatonJsonIsMissing_DoesNotCallApiOrCreateProjectFiles()
    {
        var projectRoot = CreateProjectRoot();
        var getAppCalls = 0;
        var service = new LinkAppService(
            new ConfigService(),
            (_, _, _, _) =>
            {
                getAppCalls++;
                return Task.FromResult(ParseApp("app-456", "Linked App"));
            });

        var result = await service.LinkAsync(projectRoot, "app-456", "https://automaton.monster", "ak_test", "local");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Checks.Any(check => check.Message.Contains("automaton.json is required")), Is.True);
            Assert.That(getAppCalls, Is.Zero);
            Assert.That(Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task LinkAsync_WhenProfileIsAlreadyLinkedToAnotherApp_DoesNotChangeLocalFiles()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new Automaton.Cli.Models.AutomatonConfig
        {
            Name = "Local project",
            Application = ApplicationContractTestFactory.Create()
        });
        configService.SaveNamedEnvironmentConfig(projectRoot, "local", new Automaton.Cli.Models.GlobalConfig
        {
            Server = "https://automaton.monster",
            AppId = "other-app"
        });
        var sourcePath = Path.Combine(projectRoot, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "keep me");
        var contractBefore = await File.ReadAllTextAsync(Path.Combine(projectRoot, "automaton.json"));
        var service = new LinkAppService(
            configService,
            (_, _, _, _) => Task.FromResult(ParseApp("app-456", "Linked App")));

        var result = await service.LinkAsync(projectRoot, "app-456", "https://automaton.monster", "ak_test", "local");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Checks.Any(check => check.Message.Contains("already linked to app other-app")), Is.True);
            Assert.That(File.ReadAllText(sourcePath), Is.EqualTo("keep me"));
            Assert.That(File.ReadAllText(Path.Combine(projectRoot, "automaton.json")), Is.EqualTo(contractBefore));
            Assert.That(configService.LoadNamedEnvironmentConfig(projectRoot, "local")?.AppId, Is.EqualTo("other-app"));
        });
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-linkapp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
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
