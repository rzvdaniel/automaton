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
            Server = "https://api.example.com",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
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
            },
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(1, 0, 0.1)));

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
            (_, _, _) => Task.FromResult(2),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(1, 0, 0.1)));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Build, false, "build failed with exit code 2");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Fix the build errors and rerun automaton deploy"));
    }

    [Test]
    public async Task DeployAsync_WhenBackendDeployHasNoRunner_SucceedsWithWarningsAndRunnerNextSteps()
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
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(4, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.2)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(1, 0, 0.1)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(false, 0, "No runner assigned")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.BackendRunning, Is.False);
        AssertCheck(result, DeployCheckNames.Upload, true, "uploaded 3 file(s)");
        AssertCheck(result, DeployCheckNames.BackendDeploy, false, "No runner assigned");
        Assert.That(result.NextSteps, Has.Some.EqualTo("Rerun automaton deploy with --runner <runner-id-or-name>"));
        Assert.That(result.NextSteps, Has.Some.EqualTo("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy"));
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
            Server = "https://api.example.com",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 1, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 0, 0)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.BackendRunning, Is.False);
        Assert.That(result.Uploaded?.Written, Is.EqualTo(2));
        Assert.That(result.SourceUploaded?.Written, Is.EqualTo(3));
        AssertCheck(result, DeployCheckNames.BackendDeploy, true, "no backend controllers found");
        Assert.That(result.Checks.Any(check => check.Name == "restore_point"), Is.True);
    }

    [Test]
    public async Task DeployAsync_WhenRestorePointFails_ContinuesDeploymentWithWarning()
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
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (Action<string>?)null,
            null,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 0, 0)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")),
            (_, _, _, _) => throw new InvalidOperationException("snapshot failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.Checks.Any(check => check.Name == "restore_point" && !check.Success && check.IsWarning && check.Message.Contains("snapshot failed")), Is.True);
    }

    [Test]
    public async Task DeployAsync_WhenRestorePointSkipped_ReportsNonBlockingWarning()
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
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (Action<string>?)null,
            null,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 0, 0)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")),
            (_, _, _, _) => Task.FromResult((false, "Skipped restore point creation because source files have not changed since the latest restore point.")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.Checks.Any(check => check.Name == "restore_point" && !check.Success && check.IsWarning && check.Message.Contains("Skipped restore point creation")), Is.True);
    }

    [Test]
    public async Task DeployAsync_WhenBackendUploadSucceeds_PreservesBackendUploadDetails()
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
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 1, 0.1, [
                new DeploySkippedEntry("backend/.env", "Excluded by platform backend deploy rule", "platform")
            ])),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 1, "backend deployed")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.BackendRunning, Is.True);
        Assert.That(result.BackendUploaded?.Written, Is.EqualTo(2));
        Assert.That(result.BackendUploaded?.SkippedEntries, Has.Count.EqualTo(1));
        AssertCheck(result, DeployCheckNames.BackendPackage, true, "reasons: 1 platform");
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
            Server = "https://api.example.com",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            async (_, _, _, zip, _, _) =>
            {
                await zip.DisposeAsync();
                return new DeployUploadResult(2, 0, 0);
            },
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 0, 0)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Uploaded?.Written, Is.EqualTo(2));
        AssertCheck(result, DeployCheckNames.Upload, true, "uploaded 2 file(s)");
    }

    [Test]
    public async Task DeployAsync_ReportsProgressDuringLongRunningStages()
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
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var progress = new List<string>();
        var service = new DeployService(
            configService,
            progress.Add,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 1, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(1, 0, 0.1)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 1, "backend deployed")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(progress, Has.Some.EqualTo("Uploading source files..."));
        Assert.That(progress, Has.Some.EqualTo("Source upload complete 3 source file(s), 0 ignored."));
        Assert.That(progress, Has.Some.StartWith("Uploading frontend files from frontend"));
        Assert.That(progress, Has.Some.EqualTo("Frontend upload complete 2 file(s), 1 ignored."));
        Assert.That(progress, Has.Some.StartWith("Uploading backend files from backend..."));
        Assert.That(progress, Has.Some.EqualTo("Backend upload complete 1 backend file(s), 0 ignored."));
        Assert.That(progress, Has.Some.EqualTo("Requesting backend deploy on the assigned runner..."));
        Assert.That(progress, Has.Some.EqualTo("backend deployed"));
    }

    [Test]
    public async Task DeployAsync_ReportsChecksAsEachStageCompletes()
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
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var reportedChecks = new List<DeployCheckResult>();
        var service = new DeployService(
            configService,
            (Action<string>?)null,
            (Action<DeployCheckResult>)reportedChecks.Add,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(1, 0, 0.1)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 1, "backend deployed")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(reportedChecks.Select(check => check.Name).ToArray(), Is.EqualTo(new[]
        {
            DeployCheckNames.AppLink,
            DeployCheckNames.Credentials,
            DeployCheckNames.Build,
            DeployCheckNames.FrontendPackage,
            "restore_point",
            DeployCheckNames.SourceUpload,
            DeployCheckNames.Upload,
            DeployCheckNames.BackendPackage,
            DeployCheckNames.BackendPackage,
            DeployCheckNames.BackendDeploy
        }));
    }

    [Test]
    public async Task DeployAsync_WhenUploadHasStructuredSkipReasons_IncludesReasonSummary()
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
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 1, 0.1, [
                new DeploySkippedEntry("source/.env", "Excluded by platform rule", "platform")
            ])),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 2, 0.1, [
                new DeploySkippedEntry("assets/debug.js", "Excluded by admin frontend deploy rule", "admin"),
                new DeploySkippedEntry("assets/secret.js", "Excluded by app frontend deploy rule", "app")
            ])),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 0, 0)),
            (_, _, _, _) => Task.FromResult(new DeployBackendResult(true, 0, "no backend controllers found")));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        AssertCheck(result, DeployCheckNames.Upload, true, "reasons: 1 admin, 1 app");
        AssertCheck(result, DeployCheckNames.SourceUpload, true, "reasons: 1 platform");
    }

    [Test]
    public async Task DeployAsync_WhenSourceUploadFails_ReturnsStructuredFailure()
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
                DeployDir = "frontend/dist"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => throw new InvalidOperationException("source upload failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: true, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.SourceUpload, false, "source upload failed");
    }

    [Test]
    public async Task DeployAsync_WhenBackendUploadWritesZeroFiles_FailsBeforeRunnerDeploy()
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
                DeployDir = "frontend/dist"
            },
            Backend = new BackendConfig
            {
                DeployDir = "backend"
            }
        });
        configService.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        WriteFrontendIndexHtml(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));
        File.WriteAllText(Path.Combine(projectRoot, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");

        var deployInvoked = false;
        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(3, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(2, 0, 0.1)),
            (_, _, _, _, _, _) => Task.FromResult(new DeployUploadResult(0, 1, 0.1)),
            (_, _, _, _) =>
            {
                deployInvoked = true;
                return Task.FromResult(new DeployBackendResult(true, 1, "ok"));
            });

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        Assert.That(deployInvoked, Is.False);
        AssertCheck(result, DeployCheckNames.BackendPackage, false, "backend upload wrote 0 files");
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
        _tempDirectories.Add(path);
        return path;
    }

    private static void WriteFrontendIndexHtml(string projectRoot)
    {
        var frontendDist = Path.Combine(projectRoot, "frontend", "dist");
        Directory.CreateDirectory(frontendDist);
        File.WriteAllText(Path.Combine(frontendDist, "index.html"), "<html><body>ok</body></html>");
    }
}
