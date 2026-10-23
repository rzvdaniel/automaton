using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
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

        var service = new DeployService(configService);

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        AssertCheck(result, DeployCheckNames.Credentials, false, "API key could not be resolved");
    }

    [Test]
    public async Task DeployAsync_WhenRuntimeCommandIsMissing_FailsBeforeRemoteOperations()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure(runtimeCommand: false, buildCommand: false);

        var result = await fixture.CreateService().DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            AssertCheck(result, DeployCheckNames.ApplicationPackage, false, "runtime command is required");
            Assert.That(result.NextSteps, Has.Some.Contains("application.runtime.command.executable"));
            Assert.That(fixture.SnapshotCalls, Is.Zero);
            Assert.That(fixture.DeployCalls, Is.Zero);
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
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        var progress = new List<string>();
        var service = fixture.CreateService(
            new DeployRuntimeResult(true, 1, "package deployed", Logs: ["stdout: runner build diagnostic"]),
            new DeployUploadResult(5, 0, 0.2), progress, zip => Assert.That(zip.Length, Is.GreaterThan(0)));
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
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(4, 0, 0.1),
                new DeployRuntimeResult(true, 1, "deployed"))));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuntimeRunning, Is.True);
        AssertCheck(result, DeployCheckNames.PackageDeploy, true, "deployed");
    }

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public async Task DeployAsync_WhenRuntimeDidNotDeploy_ReturnsFailure(bool runtimeSucceeded, int runnerCount)
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        const string reason = "Application container failed readiness; previous runtime was retained.";
        var service = fixture.CreateService(new DeployRuntimeResult(runtimeSucceeded, runnerCount, reason));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Summary, Is.EqualTo("Deploy failed"));
            Assert.That(result.RuntimeRunning, Is.False);
            Assert.That(result.HasWarnings, Is.False);
            Assert.That(result.Checks.Single(check => check.Name == DeployCheckNames.PackageDeploy).IsWarning, Is.False);
            Assert.That(result.NextSteps.Any(step => step.Contains("Inspect the deployment failure", StringComparison.Ordinal)), Is.True);
        });
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, reason);
    }

    [Test]
    public async Task DeployAsync_WhenManagedRuntimeIsPreparing_ReturnsManagedRuntimeNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        var service = fixture.CreateService(new DeployRuntimeResult(
            false, 0, "Managed runtime is being prepared.", "preparingManagedRuntime", true));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.HasWarnings, Is.False);
        Assert.That(result.NextSteps, Does.Contain("Managed runtime is being prepared. Rerun automaton deploy in a moment."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Managed runtime is being prepared.");
    }

    [Test]
    public async Task DeployAsync_WhenFrontendOnly_RequestsManagedRuntimeReadinessBeforeUpload()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure(name: "Generic App", includeBackend: false);
        var service = fixture.CreateService(readinessResponse:
            "{\"isReady\":false,\"state\":\"managedRunnerProvisionFailed\",\"message\":\"Managed runner could not be provisioned.\",\"requiresRetry\":true}");

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(fixture.SnapshotCalls, Is.EqualTo(1));
        Assert.That(fixture.DeployCalls, Is.Zero);
        Assert.That(result.Success, Is.False);
        Assert.That(result.NextSteps, Does.Contain("Managed runner could not be provisioned."));
        Assert.That(result.Checks.Single(check => check.Name == DeployCheckNames.RunnerAssignment).IsWarning, Is.False);
    }

    [Test]
    public async Task DeployAsync_WhenManagedRunnerRequiresProtocolUpgrade_DoesNotBlamePlan()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        var service = fixture.CreateService(new DeployRuntimeResult(
            false, 0,
            "Runner 'hosted' must be upgraded for container-based application execution before deployment.",
            "managedRuntimeUnavailable", false));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.NextSteps, Does.Contain("The assigned Runner does not support the current container-based application deployment protocol."));
        Assert.That(result.NextSteps, Does.Contain("Ask an administrator to upgrade the Hosted Runner, or assign a compatible Runner, then rerun automaton deploy."));
        Assert.That(result.NextSteps.Any(step => step.Contains("--runner", StringComparison.Ordinal)), Is.False);
        Assert.That(result.NextSteps.Any(step => step.Contains("plan", StringComparison.OrdinalIgnoreCase)), Is.False);
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Runner 'hosted' must be upgraded for container-based application execution before deployment.");
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
            (_, _, _, _, _, _, _) => Task.FromResult((
                new DeployUploadResult(1, 0, 0.1),
                new DeployRuntimeResult(
                    false,
                    0,
                    "Your subscription plan does not include a hosted runner.",
                    "managedRunnerProvisionFailed",
                    false))));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.NextSteps, Does.Contain("The active subscription plan does not have a usable Hosted Runner capability or specification."));
        Assert.That(result.NextSteps, Does.Contain("Ask an administrator to enable Hosted Runner and map an active specification to the plan, then rerun automaton deploy."));
        AssertCheck(result, DeployCheckNames.PackageDeploy, false, "Your subscription plan does not include a hosted runner.");
    }

    [Test]
    public async Task DeployAsync_WhenRunnerHostLacksInfrastructureRunner_DoesNotBlamePlan()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        var service = fixture.CreateService(new DeployRuntimeResult(
            false, 0, "Normal hosted Runner provisioning requires an Infrastructure Runner transport.",
            "managedRunnerProvisionFailed", false));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.NextSteps, Does.Contain("No eligible Runner Host is available for managed Hosted Runner provisioning."));
        Assert.That(result.NextSteps.Any(step => step.Contains("protocol-4 Infrastructure Runner with HostContainerExecution", StringComparison.Ordinal)), Is.True);
        Assert.That(result.NextSteps.Any(step => step.Contains("plan does not include", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task DeployAsync_WhenAssignedRunnerIsOffline_ReturnsAssignedRunnerNextSteps()
    {
        var projectRoot = CreateProjectRoot();
        var fixture = new DeployServiceTestProject(projectRoot);
        fixture.Configure();
        var service = fixture.CreateService(new DeployRuntimeResult(
            false, 0, "No connected runner is assigned to this app.",
            "managedRuntimeUnavailable", false));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

        Assert.That(result.Success, Is.False);
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
            (_, _, _, _, _, _, _) => throw new InvalidOperationException("package upload failed"));

        var result = await service.DeployAsync(projectRoot, clean: false, directoryOverride: null);

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
            null,
            new DeploymentTargetRunnerInfo("runner-eu", "Runner EU", true));
        var service = new DeployService(
            configService,
            deployPackageAsync: (_, _, _, targetSlug, _, _, _) =>
            {
                requestedTarget = targetSlug;
                return Task.FromResult((
                    new DeployUploadResult(1, 0, 0.1),
                    new DeployRuntimeResult(true, 1, "deployed")));
            },
            createSnapshotAsync: (_, _, _, _) =>
                Task.FromResult((true, "snapshot created")));

        var result = await service.DeployAsync(
            projectRoot,
            clean: false,
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
