using Automaton.Cli.Models;
using Automaton.Cli.Services;
using System.Text.Json;
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
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", string.Empty, Guid.NewGuid().ToString());
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
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
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
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var progress = new List<string>();
        var service = new DeployService(
            configService,
            progress.Add,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, zip, _, _) =>
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
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(4, 0, 0.1),
                new DeployBackendResult(true, 1, "ignored"))));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: true, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.SkipBackend, Is.True);
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "backend deploy skipped by flag");
    }

    [Test]
    public async Task DeployAsync_WhenManagedRuntimeIsPreparing_ReturnsManagedRuntimeNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(1, 0, 0.1),
                new DeployBackendResult(
                    false,
                    0,
                    "Managed runtime is being prepared.",
                    "preparingManagedRuntime",
                    true))));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.HasWarnings, Is.True);
        Assert.That(result.NextSteps, Does.Contain("Managed runtime is being prepared. Rerun automaton deploy in a moment."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Managed runtime is being prepared.");
    }

    [Test]
    public async Task DeployAsync_WhenFrontendOnly_RequestsManagedRuntimeReadinessBeforeUpload()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Generic App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);

        var readinessCalled = false;
        var service = new DeployService(
            configService,
            runBuildAsync: (_, _, _) => Task.FromResult(0),
            deployPackageAsync: (_, _, _, _, _, _, _) => throw new InvalidOperationException("upload must wait for runtime readiness"),
            createSnapshotAsync: (_, _, _, _) => Task.FromResult((true, "snapshot created")),
            ensureManagedRuntimeReadyAsync: (_, _, _, _) =>
            {
                readinessCalled = true;
                using var document = JsonDocument.Parse("{\"isReady\":false,\"state\":\"managedRunnerProvisionFailed\",\"message\":\"Managed runner could not be provisioned.\",\"requiresRetry\":true}");
                return Task.FromResult(document.RootElement.Clone());
            });

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(readinessCalled, Is.True);
        Assert.That(result.Success, Is.False);
        Assert.That(result.NextSteps, Does.Contain("Managed runner could not be provisioned."));
        Assert.That(result.Checks.Single(check => check.Name == DeployCheckNames.RunnerAssignment).IsWarning, Is.False);
    }

    [Test]
    public async Task DeployAsync_WhenManagedRuntimeIsUnavailable_ReturnsEntitlementNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(1, 0, 0.1),
                new DeployBackendResult(
                    false,
                    0,
                    "Managed backend runtime is not included in the current plan.",
                    "managedRuntimeUnavailable",
                    false))));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NextSteps, Does.Contain("This workspace plan does not include managed backend runtime."));
        Assert.That(result.NextSteps, Does.Contain("Assign a self-hosted runner or change the workspace plan, then rerun automaton deploy."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Managed backend runtime is not included in the current plan.");
    }

    [Test]
    public async Task DeployAsync_WhenAssignedRunnerIsOffline_ReturnsAssignedRunnerNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        WriteBackendController(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(1, 0, 0.1),
                new DeployBackendResult(
                    false,
                    0,
                    "No connected runner is assigned to this app.",
                    "managedRuntimeUnavailable",
                    false))));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.NextSteps, Does.Contain("This app is assigned to a runner, but that runner is not currently connected."));
        Assert.That(result.NextSteps, Does.Contain("Bring the assigned runner online or reassign the app to a healthy runner in Automaton Studio, then rerun automaton deploy."));
        Assert.That(result.NextSteps.Any(step => step.Contains("plan does not include", StringComparison.OrdinalIgnoreCase)), Is.False);
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "No connected runner is assigned to this app.");
    }

    [Test]
    public async Task DeployAsync_WhenPackageUploadFails_ReturnsStructuredFailure()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(configService, projectRoot, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);

        var service = new DeployService(
            configService,
            (_, _, _) => Task.FromResult(0),
            (_, _, _, _, _, _, _) => throw new InvalidOperationException("package upload failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, skipBackend: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.PackageUpload, false, "package upload failed");
    }

    [Test]
    public async Task DeployAsync_NamedTargetPassesSlugAndReportsDeploymentIdentity()
    {
        var projectRoot = CreateProjectRoot();
        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(
            configService,
            projectRoot,
            "https://api.example.com",
            "ak_test",
            Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(projectRoot);
        string? requestedTarget = null;
        var target = new DeploymentTargetInfo(
            "target-eu",
            "Server EU",
            "server-eu",
            false,
            "Running",
            true,
            null,
            new DeploymentTargetRunnerInfo("runner-eu", "Runner EU", true));
        var service = new DeployService(
            configService,
            runBuildAsync: (_, _, _) => Task.FromResult(0),
            deployPackageAsync: (_, _, _, targetSlug, _, _, _) =>
            {
                requestedTarget = targetSlug;
                return Task.FromResult((
                    new DeployUploadResult(1, 0, 0.1),
                    new DeployBackendResult(true, 1, "deployed")));
            },
            createSnapshotAsync: (_, _, _, _) =>
                Task.FromResult((true, "snapshot created")));

        var result = await service.DeployAsync(
            projectRoot,
            clean: false,
            skipBackend: true,
            directoryOverride: null,
            target: target);

        Assert.Multiple(() =>
        {
            Assert.That(requestedTarget, Is.EqualTo("server-eu"));
            Assert.That(result.TargetSlug, Is.EqualTo("server-eu"));
            Assert.That(result.RunnerName, Is.EqualTo("Runner EU"));
            Assert.That(result.DeploymentMode, Is.EqualTo("named"));
        });
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

    private static void SaveEnvironment(ConfigService configService, string projectRoot, string server, string apiKey, string appId)
    {
        configService.SaveNamedEnvironmentConfig(projectRoot, "local", new GlobalConfig
        {
            Server = server,
            ApiKey = apiKey,
            AppId = appId
        });
        configService.SaveProjectEnvironmentSelection(projectRoot, new ProjectEnvironmentSelection
        {
            Default = "local"
        });
    }
}
