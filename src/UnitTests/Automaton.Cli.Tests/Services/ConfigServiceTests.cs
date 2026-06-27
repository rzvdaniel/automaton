using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class ConfigServiceTests
{
    private readonly List<string> _tempDirectories = [];
    private string? _originalServerEnv;
    private string? _originalApiKeyEnv;

    [SetUp]
    public void SetUp()
    {
        _originalServerEnv = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        _originalApiKeyEnv = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");

        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", null);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", _originalServerEnv);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", _originalApiKeyEnv);

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
    public void SaveProjectLocalConfig_SavesConfigInsideProjectAutomatonDirectory()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        // Act
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://api.automaton.monster",
            ApiKey = "ak_test_local"
        });

        // Assert
        var configPath = configService.GetProjectLocalConfigPath(projectRoot);
        Assert.That(File.Exists(configPath), Is.True);

        var saved = configService.LoadProjectLocalConfig(projectRoot);
        Assert.That(saved.Server, Is.EqualTo("https://api.automaton.monster"));
        Assert.That(saved.ApiKey, Is.EqualTo("ak_test_local"));
    }

    [Test]
    public void RequireCredentials_WhenLocalConfigExists_UsesLocalServerAndApiKey()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        // Act
        var (server, apiKey) = configService.RequireCredentials(directory: projectRoot);

        // Assert
        Assert.That(server, Is.EqualTo("https://local.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_local"));
    }

    [Test]
    public void RequireCredentials_WhenProjectConfigHasServer_PrefersAutomatonJsonServerOverLocalConfig()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        var projectConfig = new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "Test App",
            Server = "https://project.example.com",
            Frontend = new FrontendConfig
            {
                Build = "cd frontend && npm install && npm run build",
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        };

        // Act
        var (server, apiKey) = configService.RequireCredentials(projectConfig, projectRoot);

        // Assert
        Assert.That(server, Is.EqualTo("https://project.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_local"));
    }

    [Test]
    public void ResolveExistingConfig_WhenLocalScopeRequested_PrefersEnvironmentVariablesOverProjectLocalConfig()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", "https://env.example.com");
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "ak_env");

        var resolved = configService.ResolveExistingConfig(global: false, directory: projectRoot);

        Assert.That(resolved.Server, Is.EqualTo("https://env.example.com"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_env"));
    }

    [Test]
    public void RequireCredentials_WhenEnvironmentVariablesExist_OverridesProjectConfigAndStoredCredentials()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        var projectConfig = new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "Test App",
            Server = "https://project.example.com"
        };

        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", "https://env.example.com/");
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "ak_env");

        var (server, apiKey) = configService.RequireCredentials(projectConfig, projectRoot);

        Assert.That(server, Is.EqualTo("https://env.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_env"));
    }

    [Test]
    public void RequireCredentials_WhenProjectAndLocalConfigProvideNothing_UsesProjectLocalValues()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local-only.example.com",
            ApiKey = "ak_local_only"
        });

        var (server, apiKey) = configService.RequireCredentials(directory: projectRoot);

        Assert.That(server, Is.EqualTo("https://local-only.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_local_only"));
    }

    [Test]
    public void RequireCredentials_WhenCalledFromNestedDirectory_UsesNearestProjectLocalConfig()
    {
        var projectRoot = CreateTempDirectory();
        var nestedDirectory = Path.Combine(projectRoot, "frontend", "src");
        Directory.CreateDirectory(nestedDirectory);

        var configService = new ConfigService();
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        var (server, apiKey) = configService.RequireCredentials(directory: nestedDirectory);

        Assert.That(server, Is.EqualTo("https://local.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_local"));
    }

    [Test]
    public void LoadProjectConfig_WhenCalledFromNestedDirectory_UsesNearestAutomatonJson()
    {
        var projectRoot = CreateTempDirectory();
        var nestedDirectory = Path.Combine(projectRoot, "frontend");
        Directory.CreateDirectory(nestedDirectory);

        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = "app-123",
            Name = "Nested App",
            Server = "https://project.example.com",
            Frontend = new FrontendConfig
            {
                Build = "cd frontend && npm install && npm run build",
                DeployDir = "frontend/dist"
            }
        });

        var loaded = configService.LoadProjectConfig(nestedDirectory);

        Assert.That(loaded.AppId, Is.EqualTo("app-123"));
        Assert.That(loaded.Name, Is.EqualTo("Nested App"));
        Assert.That(loaded.Server, Is.EqualTo("https://project.example.com"));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
