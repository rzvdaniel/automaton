using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using Automaton.Core.Services;
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

        var service = new DeployService(configService);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Credentials, false, "API key could not be resolved");
    }

    [Test]
    public async Task DeployAsync_WhenRuntimeCommandIsMissing_FailsBeforeRemoteOperations()
    {
        var (projectRoot, configService) = CreateProjectWithMissingRuntimeCommand();
        var snapshotCalls = 0;
        var deployCalls = 0;
        var service = new DeployService(
            configService,
            deployPackageAsync: (_, _, _, _, _, _, _) =>
            {
                deployCalls++;
                throw new AssertionException("deploy callback should not run");
            },
            createSnapshotAsync: (_, _, _, _) =>
            {
                snapshotCalls++;
                return Task.FromResult((true, "snapshot created"));
            });

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            AssertCheck(result, DeployCheckNames.ApplicationPackage, false, "runtime command is required");
            Assert.That(result.NextSteps, Has.Some.Contains("application.runtime.command.executable"));
            Assert.That(snapshotCalls, Is.Zero);
            Assert.That(deployCalls, Is.Zero);
        });
    }

    [Test]
    public async Task DeployAsync_WhenBuildIsConfigured_DefersExecutionToRunner()
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
            deployPackageAsync: (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(1, 0, 0.1),
                new DeployRuntimeResult(true, 1, "deployed"))),
            createSnapshotAsync: (_, _, _, _) => Task.FromResult((true, "snapshot created")));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        AssertCheck(result, DeployCheckNames.Build, true, "deferred to the assigned Runner");
    }

    [Test]
    public async Task DeployAsync_WhenPackageDeploySucceeds_ReturnsSuccessShape()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: true);

        var progress = new List<string>();
        var service = new DeployService(
            configService,
            progress: progress.Add,
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.SuccessfulPackage);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuntimeRunning, Is.True);
        Assert.That(result.RuntimeUploaded?.Written, Is.EqualTo(5));
        Assert.That(progress, Has.Some.EqualTo("Creating deploy package..."));
        Assert.That(progress, Has.Some.EqualTo("Uploading deploy package to the assigned runner..."));
        Assert.That(progress, Has.Some.EqualTo("stdout: runner build diagnostic"));
        AssertCheck(result, DeployCheckNames.PackageUpload, true, "uploaded 5 deploy package");
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "package deployed");
    }

    [Test]
    public async Task DeployAsync_AlwaysDeploysTheConfiguredRuntime()
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
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.SuccessfulDeployment);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuntimeRunning, Is.True);
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "deployed");
    }

    [Test]
    public async Task DeployAsync_WhenManagedRuntimeIsPreparing_ReturnsManagedRuntimeNextSteps()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: true);

        var service = new DeployService(
            configService,
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.ManagedRuntimePreparing);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);
        DeployServiceTestDoubles.AssertManagedRuntimePreparing(result);
    }

    [Test]
    public async Task DeployAsync_WhenFrontendOnly_RequestsManagedRuntimeReadinessBeforeUpload()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: false, name: "Generic App");

        await DeployServiceTestDoubles.AssertFrontendOnlyReadinessFailureAsync(projectRoot, configService);

    }

    [Test]
    public async Task DeployAsync_WhenManagedRunnerRequiresProtocolUpgrade_DoesNotBlamePlan()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: true);

        var service = new DeployService(
            configService,
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.ProtocolUpgradeRequired);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);
        DeployServiceTestDoubles.AssertProtocolUpgradeGuidance(result);
    }

    [Test]
    public async Task DeployAsync_WhenHostedRunnerPlanDoesNotIncludeCapability_ReturnsPlanSpecificNextSteps()
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
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.PlanCapabilityMissing);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.NextSteps, Does.Contain("The active subscription plan does not have a usable Hosted Runner capability or specification."));
        Assert.That(result.NextSteps, Does.Contain("Ask an administrator to enable Hosted Runner and map an active specification to the plan, then rerun automaton deploy."));
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Your subscription plan does not include a hosted runner.");
    }

    [Test]
    public async Task DeployAsync_WhenRunnerHostLacksInfrastructureRunner_DoesNotBlamePlan()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: true);

        var service = new DeployService(
            configService,
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.InfrastructureRunnerMissing);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);
        DeployServiceTestDoubles.AssertInfrastructureRunnerGuidance(result);
    }

    [Test]
    public async Task DeployAsync_WhenAssignedRunnerIsOffline_ReturnsAssignedRunnerNextSteps()
    {
        var (projectRoot, configService) = CreateConfiguredProject(includeBackend: true);

        var service = new DeployService(
            configService,
            checkReported: null,
            deployPackageAsync: DeployServiceTestDoubles.AssignedRunnerOffline);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);
        DeployServiceTestDoubles.AssertAssignedRunnerOfflineGuidance(result);
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
            (_, _, _, _, _, _, _) => throw new InvalidOperationException("package upload failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

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

    private (string Root, ConfigService Config) CreateConfiguredProject(bool includeBackend, string name = "App")
    {
        var root = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectConfig(root, new AutomatonConfig
        {
            Name = name,
            Application = ApplicationContractTestFactory.Create()
        });
        SaveEnvironment(config, root, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        WriteFrontendIndexHtml(root);
        if (includeBackend)
        {
            WriteBackendController(root);
        }

        return (root, config);
    }

    private (string Root, ConfigService Config) CreateProjectWithMissingRuntimeCommand()
    {
        var root = CreateProjectRoot();
        var config = new ConfigService();
        var application = ApplicationContractTestFactory.Create();
        application.Runtime!.Command = null;
        application.Build!.Command = null;
        config.SaveProjectConfig(root, new AutomatonConfig { Name = "App", Application = application });
        SaveEnvironment(config, root, "https://api.example.com", "ak_test", Guid.NewGuid().ToString());
        return (root, config);
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
