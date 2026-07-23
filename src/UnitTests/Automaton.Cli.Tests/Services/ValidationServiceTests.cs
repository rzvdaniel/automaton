using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class ValidationServiceTests
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
    public async Task ValidateAsync_WhenAutomatonJsonIsMissing_FailsAppLinkAndRemoteAccess()
    {
        var projectRoot = CreateProjectRoot();
        WriteFrontendIndexHtml(projectRoot);

        var service = new ValidationService(new ConfigService(), (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, ValidationCheckNames.AppLink, false, "automaton.json not found");
        AssertCheck(result, ValidationCheckNames.RemoteAppAccess, false, "skipped because app linkage failed");
        Assert.That(result.NextSteps, Has.Some.Contains("automaton new"));
        Assert.That(result.NextSteps.Any(step => step.Contains("\"frontend\"")), Is.True);
    }

    [Test]
    public async Task ValidateAsync_WhenAutomatonJsonIsPresentButUnlinked_SuggestsCreateOrLink()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        WriteFrontendIndexHtml(projectRoot);

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Unlinked App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, ValidationCheckNames.AppLink, false, "not linked to an appId");
        Assert.That(result.NextSteps, Has.Some.Contains("automaton create"));
    }

    [Test]
    public async Task ValidateAsync_WhenDeployDirectoryHasNoIndexHtml_FailsFrontendDeployShape()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Test App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "dist"));

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, ValidationCheckNames.FrontendDeployShape, false, "no index.html found");
    }

    [Test]
    public async Task ValidateAsync_WhenBackendControllersExistWithoutRunner_FailsBackendRunnerCheck()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Backend App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo(null)));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, ValidationCheckNames.RemoteAppAccess, true, "remote app found");
        AssertCheck(result, ValidationCheckNames.BackendRunner, false, "no runner is assigned");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Run automaton deploy and choose a connected runner when prompted"));
    }

    [Test]
    public async Task ValidateAsync_WhenRemoteAppAccessFails_ReportsRemoteFailure()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Remote App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);

        var service = new ValidationService(configService, (_, _, _, _) =>
            throw new InvalidOperationException("remote app not found"));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, ValidationCheckNames.RemoteAppAccess, false, "remote app not found");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Run automaton whoami to confirm the current server and identity"));
    }

    [Test]
    public async Task ValidateAsync_WhenFrontendOnlyAppIsLinkedAndReachable_Passes()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Frontend App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Checks.All(check => check.Success), Is.True);
        Assert.That(result.NextSteps, Is.Empty);
    }

    [Test]
    public async Task ValidateAsync_WhenAutomatonIgnoreIsMissing_ReportsWarningByDefault()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Frontend App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        File.Delete(Path.Combine(projectRoot, ".automatonignore"));
        WriteFrontendIndexHtml(projectRoot);

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot);

        var warning = result.Checks.Single(c => c.Name == "automatonIgnore");
        Assert.That(warning.Success, Is.True);
        Assert.That(warning.IsWarning, Is.True);
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task ValidateAsync_WhenStrictAndAutomatonIgnoreIsMissing_FailsValidation()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();

        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Frontend App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        SaveEnvironment(configService, projectRoot, "local", "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        File.Delete(Path.Combine(projectRoot, ".automatonignore"));
        WriteFrontendIndexHtml(projectRoot);

        var service = new ValidationService(configService, (_, _, _, _) =>
            Task.FromResult(new RemoteAppValidationInfo("runner-1")));

        var result = await service.ValidateAsync(projectRoot, strict: true);

        var warning = result.Checks.Single(c => c.Name == "automatonIgnore");
        Assert.That(warning.Success, Is.False);
        Assert.That(warning.IsWarning, Is.False);
        Assert.That(result.Success, Is.False);
    }

    private static void AssertCheck(ValidationResult result, string name, bool expectedSuccess, string expectedMessageFragment)
    {
        var check = result.Checks.Single(c => c.Name == name);
        Assert.That(check.Success, Is.EqualTo(expectedSuccess));
        Assert.That(check.Message, Does.Contain(expectedMessageFragment));
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static void WriteFrontendIndexHtml(string projectRoot)
    {
        var frontendDist = Path.Combine(projectRoot, "frontend", "dist");
        Directory.CreateDirectory(frontendDist);
        File.WriteAllText(Path.Combine(frontendDist, "index.html"), "<html><body>ok</body></html>");
    }

    private static void SaveEnvironment(ConfigService configService, string projectRoot, string environmentName, string server, string apiKey, string appId)
    {
        configService.SaveNamedEnvironmentConfig(projectRoot, environmentName, new GlobalConfig
        {
            Server = server,
            ApiKey = apiKey,
            AppId = appId
        });
        configService.SaveProjectEnvironmentSelection(projectRoot, new ProjectEnvironmentSelection
        {
            Default = environmentName
        });
    }
}
