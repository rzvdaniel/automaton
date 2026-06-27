using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class DeployServiceTests
{
    private readonly List<string> tempDirectories = [];
    private string? originalServerEnv;
    private string? originalApiKeyEnv;

    [SetUp]
    public void SetUp()
    {
        originalServerEnv = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        originalApiKeyEnv = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", null);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", originalServerEnv);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", originalApiKeyEnv);

        foreach (var path in tempDirectories.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }

        tempDirectories.Clear();
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
            Server = "https://api.example.com",
            Frontend = new FrontendConfig { DeployDir = "frontend/dist" }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = string.Empty });
        WriteFrontendIndexHtml(projectRoot);

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
            Frontend = new FrontendConfig
            {
                Build = "cd frontend && npm run build",
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(2));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Build, false, "build failed with exit code 2");
    }

    [Test]
    public async Task DeployAsync_WhenPackageDeploySucceeds_ReturnsSuccessShape()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com",
            Frontend = new FrontendConfig { DeployDir = "frontend/dist" },
            Backend = new BackendConfig { DeployDir = "backend" }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var progress = new List<string>();
        var service = new DeployService(
            configService,
            progress.Add,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, zip, _, _) =>
            {
                Assert.That(zip.Length, Is.GreaterThan(0));
                return Task.FromResult((
                    new DeployUploadResult(5, 0, 0.2),
                    new DeployBackendResult(true, 1, "package deployed")));
            });

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.BackendRunning, Is.True);
        Assert.That(result.Uploaded?.Written, Is.EqualTo(5));
        Assert.That(progress, Has.Some.EqualTo("Creating deploy package..."));
        Assert.That(progress, Has.Some.EqualTo("Uploading deploy package to the assigned runner..."));
        AssertCheck(result, DeployCheckNames.PackageUpload, true, "uploaded 5 deploy package");
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "package deployed");
    }

    [Test]
    public async Task DeployAsync_WhenSkipBackend_StillUploadsPackage()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com",
            Frontend = new FrontendConfig { DeployDir = "frontend/dist" },
            Backend = new BackendConfig { DeployDir = "backend" }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(4, 0, 0.1),
                new DeployBackendResult(true, 1, "ignored"))));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: true, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.SkipBackend, Is.True);
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "backend deploy skipped by flag");
    }

    [Test]
    public async Task DeployAsync_WhenPackageUploadFails_ReturnsStructuredFailure()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com",
            Frontend = new FrontendConfig { DeployDir = "frontend/dist" }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => throw new InvalidOperationException("package upload failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.PackageUpload, false, "package upload failed");
    }

    private static void AssertCheck(DeployResult result, string name, bool expectedSuccess, string expectedMessageFragment)
    {
        var check = result.Checks.Last(c => c.Name == name);
        Assert.That(check.Success, Is.EqualTo(expectedSuccess));
        Assert.That(check.Message, Does.Contain(expectedMessageFragment));
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        tempDirectories.Add(path);
        return path;
    }

    private static void WriteFrontendIndexHtml(string projectRoot)
    {
        var frontendDist = Path.Combine(projectRoot, "frontend", "dist");
        Directory.CreateDirectory(frontendDist);
        File.WriteAllText(Path.Combine(frontendDist, "index.html"), "<html><body>ok</body></html>");
    }

    private static void WriteBackendController(string projectRoot)
    {
        var backend = Path.Combine(projectRoot, "backend");
        Directory.CreateDirectory(backend);
        File.WriteAllText(Path.Combine(backend, "todos.py"), "def handle(action_name, inputs):\n    return {'success': True}\n");
    }
}
