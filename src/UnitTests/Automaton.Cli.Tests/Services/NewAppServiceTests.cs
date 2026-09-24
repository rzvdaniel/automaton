using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.AiGuidance;
using Automaton.Core.Models;
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
        var result = await CreateSuccessfulService().CreateAsync(
            projectRoot, "My App", "https://automaton.monster", "ak_test", "local");

        AssertSuccessfulScaffold(result, projectRoot);
    }

    [Test]
    public async Task CreateAsync_WhenRemoteCreationFails_LeavesLocalScaffoldForLaterLinking()
    {
        var projectRoot = CreateProjectRoot();
        var result = await CreateFailingService().CreateAsync(
            projectRoot, "My App", "https://automaton.monster", "ak_test", "local");

        AssertRemoteCreationFailure(result, projectRoot);
    }

    [Test]
    public async Task CreateAsync_WhenRecoveringExistingUnlinkedScaffold_PreservesExistingApplicationContract()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Recovered App",
            Application = ApplicationContractTestFactory.Create("custom-dist")
        });

        var service = new NewAppService(
            configService,
            (_, _, name, _) => Task.FromResult(((string)"app-123", name)),
            (_, _, _) => Task.FromResult(GetReferenceSources()));

        var result = await service.CreateAsync(projectRoot, "Recovered App", "https://automaton.monster", "ak_test", "local");

        Assert.That(result.Success, Is.True);
        var config = configService.LoadProjectConfig(projectRoot);
        Assert.That(config.Application.Build.OutputDirectory, Is.EqualTo("custom-dist"));
        Assert.That(config.Application.Build.Command?.Executable, Is.EqualTo("npm"));
        Assert.That(config.Application.Build.Command?.Arguments, Is.EqualTo(new[] { "run", "build" }));
        var env = configService.LoadNamedEnvironmentConfig(projectRoot, "local");
        Assert.That(env!.AppId, Is.EqualTo("app-123"));
    }

    [Test]
    public async Task CreateAsync_WhenEnvironmentIsOmitted_WritesLiveEnvironmentByDefault()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(
            new ConfigService(),
            (_, _, name, _) => Task.FromResult(((string)"app-live", name)),
            (_, _, _) => Task.FromResult(GetReferenceSources()));

        var result = await service.CreateAsync(projectRoot, "My App", "https://api.automaton.monster", "ak_live");

        Assert.That(result.Success, Is.True);

        var configService = new ConfigService();
        var live = configService.LoadNamedEnvironmentConfig(projectRoot, ConfigService.DefaultEnvironmentName);
        var selection = configService.LoadProjectEnvironmentSelection(projectRoot);

        Assert.That(live, Is.Not.Null);
        Assert.That(live!.AppId, Is.EqualTo("app-live"));
        Assert.That(selection.Default, Is.EqualTo(ConfigService.DefaultEnvironmentName));
    }

    [Test]
    public async Task CreateAsync_WhenCredentialsAreMissing_ReturnsFailure()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.CreateAsync(projectRoot, "My App", null, null, "local");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "credentials" && !check.Success), Is.True);
    }

    [Test]
    public async Task CreateAsync_WhenNameIsMissing_ReturnsFailureBeforeApiCall()
    {
        var projectRoot = CreateProjectRoot();
        var service = new NewAppService(new ConfigService(), (_, _, _, _) => throw new InvalidOperationException("should not be called"));

        var result = await service.CreateAsync(projectRoot, null, "https://automaton.monster", "ak_test", "local");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == "createApp" && !check.Success && check.Message.Contains("app name is required")), Is.True);
    }

    [Test]
    public async Task CreateAsync_WhenAutomatonJsonIsInvalid_DoesNotOverwriteItOrCreateRemoteApp()
    {
        var projectRoot = CreateProjectRoot();
        var configPath = Path.Combine(projectRoot, "automaton.json");
        const string invalidJson = "{ invalid";
        await File.WriteAllTextAsync(configPath, invalidJson);
        var createCalls = 0;
        var service = new NewAppService(
            new ConfigService(),
            (_, _, _, _) =>
            {
                createCalls++;
                return Task.FromResult(((string)"app-123", "My App"));
            });

        var result = await service.CreateAsync(projectRoot, "My App", "https://api.example.com", "ak_test");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Checks.Any(check => check.Message.Contains("automaton.json exists but is invalid")), Is.True);
            Assert.That(createCalls, Is.Zero);
            Assert.That(File.ReadAllText(configPath), Is.EqualTo(invalidJson));
        });
    }

    [Test]
    public async Task CreateAsync_WhenSelectedProfileExists_UsesItInsteadOfLive()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveNamedEnvironmentConfig(projectRoot, "staging", new GlobalConfig
        {
            Server = "https://staging.example.com",
            ApiKey = "ak_staging"
        });
        configService.SaveProjectEnvironmentSelection(projectRoot, new ProjectEnvironmentSelection { Default = "staging" });
        var service = new NewAppService(
            configService,
            (_, _, name, _) => Task.FromResult(((string)"app-staging", name)));

        var result = await service.CreateAsync(projectRoot, "My App", "https://api.example.com", "ak_cli");

        Assert.That(result.Success, Is.True);
        Assert.That(configService.LoadNamedEnvironmentConfig(projectRoot, "staging")?.AppId, Is.EqualTo("app-staging"));
        Assert.That(configService.LoadNamedEnvironmentConfig(projectRoot, "live"), Is.Null);
        Assert.That(configService.LoadProjectEnvironmentSelection(projectRoot).Default, Is.EqualTo("staging"));
    }

    private static NewAppService CreateSuccessfulService()
    {
        var references = GetReferenceSources();
        return new NewAppService(
            new ConfigService(),
            (_, _, name, _) => Task.FromResult(("app-123", name)),
            (_, _, _) => Task.FromResult(references));
    }

    private static NewAppService CreateFailingService()
    {
        var references = GetReferenceSources();
        return new NewAppService(
            new ConfigService(),
            (_, _, _, _) => throw new HttpRequestException("API error 500 Internal Server Error"),
            (_, _, _) => Task.FromResult(references));
    }

    private static void AssertSuccessfulScaffold(NewAppResult result, string projectRoot)
    {
        Assert.That(result.Success, Is.True);
        Assert.That(result.AppId, Is.EqualTo("app-123"));
        Assert.That(result.Name, Is.EqualTo("My App"));
        Assert.That(result.Checks.Any(check => check.Name == "aiGuidance" && check.Success), Is.True);
        AssertScaffoldFiles(projectRoot);
        AssertScaffoldConfig(projectRoot);
    }

    private static void AssertScaffoldFiles(string projectRoot)
    {
        Assert.That(File.Exists(Path.Combine(projectRoot, "automaton.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automatonignore")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references")), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(projectRoot, "AGENTS.md")), Does.Contain("automaton validate"));
        var skillPath = Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md");
        Assert.That(File.Exists(skillPath), Is.True);
        var skill = File.ReadAllText(skillPath);
        Assert.That(skill, Does.Contain("Prefer a Standard or Hosted Runner for ordinary applications"));
        Assert.That(skill, Does.Contain("automaton target approve <environment>"));
        Assert.That(skill, Does.Contain("automaton target revoke <environment>"));
        Assert.That(skill, Does.Not.Contain("--acknowledge-privileged-host-access"));
    }

    private static void AssertScaffoldConfig(string projectRoot)
    {
        var configService = new ConfigService();
        var config = configService.LoadProjectConfig(projectRoot);
        Assert.That(config.Name, Is.EqualTo("My App"));
        Assert.That(config.Application, Is.Not.Null);
        Assert.That(ApplicationContractValidator.Validate(config.Application), Is.Empty);
        Assert.That(config.AppId, Is.Null);
        Assert.That(config.Application.Storage.DataDirectory, Is.EqualTo("data"));
        Assert.That(config.Application.Build.Command, Is.Null);
        var environment = configService.LoadNamedEnvironmentConfig(projectRoot, "local");
        Assert.That(environment?.AppId, Is.EqualTo("app-123"));
        Assert.That(environment?.Server, Is.EqualTo("https://automaton.monster"));
        Assert.That(environment?.ApiKey, Is.Null);
    }

    private static void AssertRemoteCreationFailure(NewAppResult result, string projectRoot)
    {
        Assert.That(result.Success, Is.False);
        Assert.That(result.ConfigPath, Is.EqualTo(Path.Combine(projectRoot, "automaton.json")));
        Assert.That(result.Checks.Any(check => check.Name == CommandDiagnosticCheckNames.LocalScaffold && check.Success), Is.True);
        Assert.That(result.Checks.Any(check => check.Name == CommandDiagnosticCheckNames.AiGuidance && check.Success), Is.True);
        Assert.That(result.Checks.Any(check => check.Name == CommandDiagnosticCheckNames.CreateApp && !check.Success), Is.True);
        Assert.That(result.NextSteps, Has.Some.EqualTo("Retry remote app creation with automaton create"));
        AssertFailureScaffold(projectRoot);
    }

    private static void AssertFailureScaffold(string projectRoot)
    {
        var config = new ConfigService().TryLoadProjectConfig(projectRoot);
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.AppId, Is.Null);
        Assert.That(config.Name, Is.EqualTo("My App"));
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automatonignore")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-newapp-tests", Guid.NewGuid().ToString("N"));
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
}
