using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class DeployServiceTests
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
    public async Task DeployAsync_WhenCredentialsAreMissing_FailsBeforeBuild()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com"
        });
        WriteIndexHtml(projectRoot);

        var buildInvoked = false;
        var service = new DeployService(
            configService,
            (_, _, _) =>
            {
                buildInvoked = true;
                return Task.FromResult(0);
            });

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        Assert.That(buildInvoked, Is.False);
        AssertCheck(result, DeployCheckNames.Credentials, false, "API key could not be resolved");
    }

    [Test]
    public async Task DeployAsync_WhenBuildFails_ReturnsStructuredBuildFailure()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com",
            Build = "npm run build"
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(2));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Build, false, "build failed with exit code 2");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Fix the build errors and rerun automaton deploy"));
    }

    [Test]
    public async Task DeployAsync_WhenBackendDeployHasNoRunner_FailsWithRunnerNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com"
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.2)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(false, 0, "No runner assigned")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Upload, true, "uploaded 3 file(s)");
        AssertCheck(result, DeployCheckNames.BackendDeploy, false, "No runner assigned");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Run automaton runners"));
        Assert.That(result.NextSteps, Has.Some.EqualTo("Run automaton set-runner <runner-id>"));
    }

    [Test]
    public async Task DeployAsync_WhenFrontendOnlyDeploySucceeds_ReturnsSuccessShape()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com"
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 1, 0.1)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Uploaded?.Written, Is.EqualTo(2));
        AssertCheck(result, DeployCheckNames.BackendDeploy, true, "no backend controllers found");
    }

    [Test]
    public async Task DeployAsync_WhenUploadDisposesStream_StillReturnsSuccessShape()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com"
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            async (_, _, _, zip, _, _) =>
            {
                await zip.DisposeAsync();
                return new DeployUploadResult(2, 0, 0);
            },
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Uploaded?.Written, Is.EqualTo(2));
        AssertCheck(result, DeployCheckNames.Upload, true, "uploaded 2 file(s)");
    }

    private static void AssertCheck(DeployResult result, string name, bool expectedSuccess, string expectedMessageFragment)
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

    private static void WriteIndexHtml(string projectRoot)
    {
        File.WriteAllText(Path.Combine(projectRoot, "index.html"), "<html><body>ok</body></html>");
    }
}
