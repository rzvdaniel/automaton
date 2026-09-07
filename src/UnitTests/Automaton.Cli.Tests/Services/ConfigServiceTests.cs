using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using NUnit.Framework;
using System.Text.Json;

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
    public void SaveProjectConfig_PersistsAndLoadsTheStackNeutralApplicationContract()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();
        var contract = new ApplicationContract
        {
            Build = new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "build-tool",
                    Arguments = ["--target", "release", "name with spaces"],
                    WorkingDirectory = "tools/build",
                    Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "release" }
                },
                OutputDirectory = "artifacts/site"
            },
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec
                {
                    Executable = "custom-server",
                    Arguments = ["--port", "4312"],
                    WorkingDirectory = "services/web",
                    Environment = new Dictionary<string, string> { ["APP_MODE"] = "production" }
                },
                Port = 4312
            },
            Health = new ApplicationHealthSpec { Path = "/ready", Port = 4312 },
            Storage = new ApplicationStorageSpec
            {
                DataDirectory = "var/data",
                Mounts = [new ApplicationStorageMount { Name = "uploads", RelativePath = "var/uploads" }]
            }
        };

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Generic App",
            Application = contract
        });

        var loaded = configService.LoadProjectConfig(projectRoot);

        Assert.That(loaded.Application.Build.Command?.Executable, Is.EqualTo("build-tool"));
        Assert.That(loaded.Application.Build.Command?.Arguments, Is.EqualTo(new[] { "--target", "release", "name with spaces" }));
        Assert.That(loaded.Application.Build.Command?.WorkingDirectory, Is.EqualTo("tools/build"));
        Assert.That(loaded.Application.Runtime.Command?.Executable, Is.EqualTo("custom-server"));
        Assert.That(loaded.Application.Runtime.Port, Is.EqualTo(4312));
        Assert.That(loaded.Application.Storage.Mounts.Single().RelativePath, Is.EqualTo("var/uploads"));
    }

    [Test]
    public void TryLoadProjectConfig_RejectsTheRemovedFrontendBackendContract()
    {
        var projectRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), """
        {
          "name": "Legacy app",
          "frontend": { "build": "npm run build", "deployDir": "dist" },
          "backend": { "deployDir": "backend" }
        }
        """);

        var loaded = new ConfigService().TryLoadProjectConfig(projectRoot);

        Assert.That(loaded, Is.Null);
    }

    [Test]
    public void ApplicationContractValidator_RejectsWorkspaceEscapesButAllowsArbitraryExecutables()
    {
        var contract = new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec
                {
                    Executable = "binary-with-any-name",
                    WorkingDirectory = "services/runtime",
                    Arguments = ["--flag", "value"]
                }
            }
        };

        Assert.That(ApplicationContractValidator.Validate(contract), Is.Empty);

        contract.Runtime.Command!.WorkingDirectory = "../outside";
        Assert.That(ApplicationContractValidator.Validate(contract), Has.Some.Contains("workingDirectory"));
    }

    [TestCase("dotnet", true)]
    [TestCase("tools/my-server", true)]
    [TestCase("./bin/server", true)]
    [TestCase("../server", false)]
    [TestCase("bin/../../server", false)]
    [TestCase("/usr/bin/server", false)]
    [TestCase("C:\\server.exe", false)]
    public void ApplicationContractValidator_ValidatesExecutablePathBoundaries(string executable, bool expectedValid)
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Runtime.Image = "test/runtime:1";
        contract.Runtime.Command = new ApplicationCommandSpec { Executable = executable };

        var errors = ApplicationContractValidator.Validate(contract);

        Assert.That(errors.Any(error => error.Contains("runtime.command.executable")), Is.EqualTo(!expectedValid));
    }

    [Test]
    public void ApplicationContractValidator_ReportsNullCommandCollectionsInsteadOfThrowing()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Runtime.Command = new ApplicationCommandSpec
        {
            Executable = "dotnet",
            Arguments = null!,
            Environment = null!
        };
        contract.Storage.Mounts = null!;

        var errors = ApplicationContractValidator.Validate(contract);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Some.EqualTo("runtime.command.arguments must be an array"));
            Assert.That(errors, Has.Some.EqualTo("runtime.command.environment must be an object"));
            Assert.That(errors, Has.Some.EqualTo("storage.mounts must be an array"));
        });

        var json = JsonSerializer.Serialize(contract, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplicationContractSerialization.Deserialize(json));
        Assert.That(exception!.Message, Does.Contain("runtime.command.arguments must be an array"));
    }

    [Test]
    public void ApplicationContractValidator_RejectsEnvironmentNameCollisionsAfterNormalization()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Storage.Mounts =
        [
            new ApplicationStorageMount { Name = "uploads", RelativePath = "uploads" },
            new ApplicationStorageMount { Name = "UPLOADS", RelativePath = "other" }
        ];

        var errors = ApplicationContractValidator.Validate(contract);

        Assert.That(errors, Has.Some.EqualTo(
            "storage.mounts.name must be unique after environment-variable normalization"));
    }

    [Test]
    public void ApplicationContractValidator_RejectsUnsupportedReadOnlyMounts()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Storage.Mounts =
        [new ApplicationStorageMount { Name = "uploads", RelativePath = "uploads", ReadOnly = true }];

        var errors = ApplicationContractValidator.Validate(contract);

        Assert.That(errors, Has.Some.EqualTo("storage.mounts.readOnly is not supported by this runner"));
    }

    [Test]
    public void ApplicationContract_AllowsPortReadinessWithoutHealthPath()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Health.Path = null;

        Assert.That(ApplicationContractValidator.Validate(contract), Is.Empty);
    }

    [Test]
    public void CanonicalApplicationContractHasNoFrontendOrBackendRequirement()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(ApplicationContract).GetProperty("Frontend"), Is.Null);
            Assert.That(typeof(ApplicationContract).GetProperty("Backend"), Is.Null);
            Assert.That(typeof(AutomatonConfig).GetProperty("Frontend"), Is.Null);
            Assert.That(typeof(AutomatonConfig).GetProperty("Backend"), Is.Null);
        });
    }

    [Test]
    public void ApplicationContract_RequiresAnExplicitRuntimeImageForRuntimeCommands()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Runtime.Command = new ApplicationCommandSpec { Executable = "app-server" };

        Assert.That(ApplicationContractValidator.Validate(contract), Is.Empty);
        Assert.That(ApplicationContractValidator.ValidateForDeployment(contract), Has.Some.EqualTo(
            "runtime.image is required when runtime.command is declared for container deployment"));

        contract.Runtime.Image = "runtime-image:1";
        Assert.That(ApplicationContractValidator.ValidateForDeployment(contract), Is.Empty);
    }

    [Test]
    public void ApplicationContract_UsesRuntimeImageAsBuildImageFallback()
    {
        var contract = ApplicationContractDefaults.Create();
        contract.Runtime.Image = "runtime-image:1";
        contract.Runtime.Command = new ApplicationCommandSpec { Executable = "app-server" };
        contract.Build.Command = new ApplicationCommandSpec { Executable = "build-tool" };

        Assert.That(ApplicationContractValidator.Validate(contract), Is.Empty);
        Assert.That(contract.Build.Image, Is.Null);
        Assert.That(contract.Runtime.Image, Is.EqualTo("runtime-image:1"));
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
    public void ProjectConfig_PreservesPendingCliAuthorizationCompletion()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();
        var requestId = Guid.NewGuid();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://api.example.com",
            ApiKey = "ak_developer_test",
            PendingAuthorizationCompletion = new PendingCliAuthorizationCompletion
            {
                RequestId = requestId,
                Server = "https://api.example.com",
                Verifier = "verifier"
            }
        });

        var saved = configService.LoadProjectLocalConfig(projectRoot);
        Assert.That(saved.PendingAuthorizationCompletion, Is.Not.Null);
        Assert.That(saved.PendingAuthorizationCompletion!.RequestId, Is.EqualTo(requestId));
        Assert.That(saved.PendingAuthorizationCompletion.Verifier, Is.EqualTo("verifier"));
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
    public void RequireCredentials_WhenLocalConfigExists_UsesLocalServerOverAutomatonJsonLegacyServer()
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
            Application = ApplicationContractTestFactory.Create()
        };

        // Act
        var (server, apiKey) = configService.RequireCredentials(projectConfig, projectRoot);

        // Assert
        Assert.That(server, Is.EqualTo("https://local.example.com"));
        Assert.That(apiKey, Is.EqualTo("ak_local"));
    }

    [Test]
    public void ResolveExistingConfig_WhenLocalScopeRequested_UsesProjectLocalConfig()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local"
        });

        var resolved = configService.ResolveExistingConfig(global: false, directory: projectRoot);

        Assert.That(resolved.Server, Is.EqualTo("https://local.example.com"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_local"));
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
            Application = ApplicationContractTestFactory.Create()
        });

        var loaded = configService.LoadProjectConfig(nestedDirectory);

        Assert.That(loaded.AppId, Is.EqualTo("app-123"));
        Assert.That(loaded.Name, Is.EqualTo("Nested App"));
        Assert.That(loaded.Server, Is.EqualTo("https://project.example.com"));
    }

    [Test]
    public void ResolveContext_WhenNamedEnvironmentProvided_UsesNamedEnvironmentCredentials()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();
        WriteEnvironment(projectRoot, "local", new GlobalConfig
        {
            Server = "https://api.automaton.test:8443",
            ApiKey = "ak_local_env"
        });

        var resolved = configService.ResolveContext(directory: projectRoot, environmentName: "local");

        Assert.That(resolved.EnvironmentName, Is.EqualTo("local"));
        Assert.That(resolved.Server, Is.EqualTo("https://api.automaton.test:8443"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_local_env"));
    }

    [Test]
    public void ResolveContext_WhenNamedEnvironmentProvided_PrefersEnvironmentOverProjectAndLocalConfig()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();
        WriteEnvironment(projectRoot, "local", new GlobalConfig
        {
            Server = "https://api.automaton.test:8443",
            ApiKey = "ak_local_env"
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local_config"
        });

        var projectConfig = new AutomatonConfig
        {
            AppId = "app-123",
            Name = "App",
            Server = "https://project.example.com"
        };

        var resolved = configService.ResolveContext(projectConfig, projectRoot, "local");

        Assert.That(resolved.Server, Is.EqualTo("https://api.automaton.test:8443"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_local_env"));
    }

    [Test]
    public void ResolveContext_WhenDefaultEnvironmentSelectionExists_UsesSelectedEnvironment()
    {
        var projectRoot = CreateTempDirectory();
        WriteEnvironment(projectRoot, "live", new GlobalConfig
        {
            Server = "https://api.automaton.monster",
            ApiKey = "ak_live_env"
        });
        WriteDefaultEnvironment(projectRoot, "live");

        var resolved = new ConfigService().ResolveContext(directory: projectRoot);

        Assert.That(resolved.EnvironmentName, Is.EqualTo("live"));
        Assert.That(resolved.Server, Is.EqualTo("https://api.automaton.monster"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_live_env"));
    }

    [Test]
    public void ResolveContext_WhenLiveEnvironmentExistsWithoutExplicitSelection_UsesLiveByDefault()
    {
        var projectRoot = CreateTempDirectory();
        WriteEnvironment(projectRoot, "live", new GlobalConfig
        {
            Server = "https://api.automaton.monster",
            ApiKey = "ak_live_env"
        });

        var resolved = new ConfigService().ResolveContext(directory: projectRoot);

        Assert.That(resolved.EnvironmentName, Is.EqualTo("live"));
        Assert.That(resolved.Server, Is.EqualTo("https://api.automaton.monster"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_live_env"));
    }

    [Test]
    public void ResolveContext_WhenEnvironmentVariablesExist_OverridesNamedEnvironment()
    {
        var projectRoot = CreateTempDirectory();
        WriteEnvironment(projectRoot, "local", new GlobalConfig
        {
            Server = "https://api.automaton.test:8443",
            ApiKey = "ak_local_env"
        });
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", "https://env.example.com/");
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "ak_env");

        var resolved = new ConfigService().ResolveContext(directory: projectRoot, environmentName: "local");

        Assert.That(resolved.EnvironmentName, Is.EqualTo("local"));
        Assert.That(resolved.Server, Is.EqualTo("https://env.example.com"));
        Assert.That(resolved.ApiKey, Is.EqualTo("ak_env"));
    }

    [Test]
    public void ResolveContext_CarriesPendingCompletionFromTheSelectedNamedEnvironment()
    {
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();
        var requestId = Guid.NewGuid();
        WriteEnvironment(projectRoot, "local", new GlobalConfig
        {
            Server = "https://local.example.com",
            ApiKey = "ak_local",
            PendingAuthorizationCompletion = new PendingCliAuthorizationCompletion
            {
                RequestId = requestId,
                Server = "https://local.example.com",
                Verifier = "local-verifier"
            }
        });

        var resolved = configService.ResolveContext(directory: projectRoot, environmentName: "local");

        Assert.That(resolved.CredentialSource, Is.EqualTo(CliCredentialSource.NamedEnvironment));
        Assert.That(resolved.PendingAuthorizationCompletion?.RequestId, Is.EqualTo(requestId));
        Assert.That(resolved.PendingAuthorizationCompletion?.Verifier, Is.EqualTo("local-verifier"));
    }

    [Test]
    public void ResolveContext_WhenNamedEnvironmentDoesNotExist_ThrowsClearError()
    {
        var projectRoot = CreateTempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ConfigService().ResolveContext(directory: projectRoot, environmentName: "missing"));

        Assert.That(ex!.Message, Does.Contain("Environment 'missing' was not found."));
        Assert.That(ex.Message, Does.Contain(".automaton"));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static void WriteEnvironment(string projectRoot, string environmentName, GlobalConfig config)
    {
        var directory = Path.Combine(projectRoot, ".automaton", "environments");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{environmentName}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private static void WriteDefaultEnvironment(string projectRoot, string environmentName)
    {
        var directory = Path.Combine(projectRoot, ".automaton");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "environment.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ProjectEnvironmentSelection
        {
            Default = environmentName
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
