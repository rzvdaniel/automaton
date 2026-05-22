using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class DeployPreparationServiceTests
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
    public async Task PrepareAsync_WhenBackendAppAlreadyHasRunner_ReturnsSuccessWithoutAssignment()
    {
        var projectRoot = CreateProjectRoot(withBackend: true);
        var config = CreateConfiguredService(projectRoot);
        var service = new DeployPreparationService(
            config,
            (_, _, _, _) => Task.FromResult(new RemoteAppValidationInfo("runner-1")),
            (_, _, _) => Task.FromResult<IReadOnlyList<RunnerSelectionItem>>([]),
            (_, _, _, _, _) => throw new InvalidOperationException("should not assign"));

        var result = await service.PrepareAsync(projectRoot, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RunnerId, Is.EqualTo("runner-1"));
        Assert.That(result.Checks.Any(check => check.Name == DeployCheckNames.RunnerAssignment && check.Success), Is.True);
    }

    [Test]
    public async Task PrepareAsync_WhenSingleConnectedRunnerExists_AssignsItAutomatically()
    {
        var projectRoot = CreateProjectRoot(withBackend: true);
        var config = CreateConfiguredService(projectRoot);
        string? assignedRunner = null;
        var service = new DeployPreparationService(
            config,
            (_, _, _, _) => Task.FromResult(new RemoteAppValidationInfo(null)),
            (_, _, _) => Task.FromResult<IReadOnlyList<RunnerSelectionItem>>([new RunnerSelectionItem("runner-2", "Runner Two", true)]),
            (_, _, _, runnerId, _) =>
            {
                assignedRunner = runnerId;
                return Task.CompletedTask;
            });

        var result = await service.PrepareAsync(projectRoot, null);

        Assert.That(result.Success, Is.True);
        Assert.That(assignedRunner, Is.EqualTo("runner-2"));
        Assert.That(result.RunnerName, Is.EqualTo("Runner Two"));
    }

    [Test]
    public async Task PrepareAsync_WhenMultipleConnectedRunnersExist_ReturnsChoices()
    {
        var projectRoot = CreateProjectRoot(withBackend: true);
        var config = CreateConfiguredService(projectRoot);
        var service = new DeployPreparationService(
            config,
            (_, _, _, _) => Task.FromResult(new RemoteAppValidationInfo(null)),
            (_, _, _) => Task.FromResult<IReadOnlyList<RunnerSelectionItem>>([
                new RunnerSelectionItem("runner-2", "Runner Two", true),
                new RunnerSelectionItem("runner-3", "Runner Three", true)]),
            (_, _, _, _, _) => Task.CompletedTask);

        var result = await service.PrepareAsync(projectRoot, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RequiresRunnerSelection, Is.True);
        Assert.That(result.AvailableRunners, Has.Count.EqualTo(2));
        Assert.That(result.Checks.Any(check => check.Name == DeployCheckNames.RunnerLookup && !check.Success && check.IsWarning), Is.True);
    }

    [Test]
    public async Task PrepareAsync_WhenNoConnectedRunnerExists_ReturnsWarningButDoesNotBlockDeploy()
    {
        var projectRoot = CreateProjectRoot(withBackend: true);
        var config = CreateConfiguredService(projectRoot);
        var service = new DeployPreparationService(
            config,
            (_, _, _, _) => Task.FromResult(new RemoteAppValidationInfo(null)),
            (_, _, _) => Task.FromResult<IReadOnlyList<RunnerSelectionItem>>([]),
            (_, _, _, _, _) => Task.CompletedTask);

        var result = await service.PrepareAsync(projectRoot, null);

        Assert.That(result.Success, Is.True);
        Assert.That(result.RequiresRunnerSelection, Is.False);
        Assert.That(result.Checks.Any(check => check.Name == DeployCheckNames.RunnerLookup && !check.Success && check.IsWarning), Is.True);
    }

    [Test]
    public async Task PrepareAsync_WhenPreferredRunnerMatches_AssignsRequestedRunner()
    {
        var projectRoot = CreateProjectRoot(withBackend: true);
        var config = CreateConfiguredService(projectRoot);
        string? assignedRunner = null;
        var service = new DeployPreparationService(
            config,
            (_, _, _, _) => Task.FromResult(new RemoteAppValidationInfo(null)),
            (_, _, _) => Task.FromResult<IReadOnlyList<RunnerSelectionItem>>([
                new RunnerSelectionItem("runner-2", "Runner Two", true),
                new RunnerSelectionItem("runner-3", "Runner Three", true)]),
            (_, _, _, runnerId, _) =>
            {
                assignedRunner = runnerId;
                return Task.CompletedTask;
            });

        var result = await service.PrepareAsync(projectRoot, "runner-3");

        Assert.That(result.Success, Is.True);
        Assert.That(assignedRunner, Is.EqualTo("runner-3"));
    }

    private string CreateProjectRoot(bool withBackend)
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-deployprep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var frontendDist = Path.Combine(path, "frontend", "dist");
        Directory.CreateDirectory(frontendDist);
        File.WriteAllText(Path.Combine(frontendDist, "index.html"), "<html><body>ok</body></html>");
        if (withBackend)
        {
            Directory.CreateDirectory(Path.Combine(path, "backend"));
            File.WriteAllText(Path.Combine(path, "backend", "todos.py"), "def handle(action_name, inputs):\n    return {'success': True, 'data': {}, 'error': ''}\n");
        }
        _tempDirectories.Add(path);
        return path;
    }

    private static ConfigService CreateConfiguredService(string projectRoot)
    {
        var config = new ConfigService();
        config.SaveProjectConfig(projectRoot, new AutomatonConfig
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
        config.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = "ak_test" });
        return config;
    }
}
