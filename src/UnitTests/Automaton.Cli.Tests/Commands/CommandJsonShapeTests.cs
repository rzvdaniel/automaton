using Automaton.Cli.Commands;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CommandJsonShapeTests
{
    private readonly List<string> _tempDirectories = [];
    private string? _originalServerEnv;
    private string? _originalApiKeyEnv;
    private string? _originalDirectory;

    [SetUp]
    public void SetUp()
    {
        _originalServerEnv = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        _originalApiKeyEnv = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        _originalDirectory = Directory.GetCurrentDirectory();

        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", null);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", null);
        Environment.ExitCode = 0;
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", _originalServerEnv);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", _originalApiKeyEnv);

        if (!string.IsNullOrWhiteSpace(_originalDirectory) && Directory.Exists(_originalDirectory))
        {
            Directory.SetCurrentDirectory(_originalDirectory);
        }

        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        _tempDirectories.Clear();
        Environment.ExitCode = 0;
    }

    [Test]
    public async Task PublishCommand_WhenProjectIsMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = PublishCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json", "--yes"]);

        AssertDiagnosticFailure(output, "Publish failed", CommandDiagnosticCheckNames.AppLink, "checks", "nextSteps");
    }

    [Test]
    public async Task StatusCommand_WhenCredentialsAreMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App",
        });
        config.SaveNamedEnvironmentConfig(projectRoot, "local", new GlobalConfig { Server = "https://api.example.com", AppId = Guid.NewGuid().ToString() });
        config.SaveProjectEnvironmentSelection(projectRoot, new ProjectEnvironmentSelection { Default = "local" });
        config.SaveProjectLocalConfig(projectRoot, new GlobalConfig { ApiKey = string.Empty });

        var command = StatusCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Status failed", CommandDiagnosticCheckNames.Credentials, "checks", "nextSteps");
    }

    [Test]
    public async Task StatusCommand_WhenNamedEnvironmentIsMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "App"
        });

        var command = StatusCommand.Build(new ConfigService());
        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json", "--env", "missing"]);

        using var doc = JsonDocument.Parse(output);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(doc.RootElement.GetProperty("summary").GetString(), Is.EqualTo("Status failed"));
    }

    [Test]
    public async Task RunnersCommand_WhenCredentialsAreMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        new ConfigService().SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = string.Empty,
            ApiKey = string.Empty
        });
        var command = RunnersCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Runner listing failed", CommandDiagnosticCheckNames.Credentials, "checks", "nextSteps");
    }

    [Test]
    public async Task LoginCommand_WhenEnvironmentKeyIsMissing_ReturnsDiagnosticJsonFailure()
    {
        var projectRoot = CreateProjectRoot();
        new ConfigService().SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = ConfigService.DefaultHostedServer,
            ApiKey = string.Empty
        });
        var command = LoginCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json", "--key-from-env"]);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("summary").GetString(), Is.EqualTo("Login failed"));

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        var authenticationCheck = checks.Single(check =>
            string.Equals(check.GetProperty("name").GetString(), CommandDiagnosticCheckNames.Authentication, StringComparison.Ordinal));

        Assert.That(authenticationCheck.GetProperty("error").GetString(), Is.EqualTo("AUTOMATON_API_KEY is not set."));

        var nextSteps = root.GetProperty("nextSteps").EnumerateArray().Select(step => step.GetString()).ToList();
        Assert.That(nextSteps, Has.Some.Contains("browser/device authorization"));
    }

    [Test]
    public async Task WhoAmICommand_WhenNamedEnvironmentIsMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = WhoAmICommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json", "--env", "missing"]);

        using var doc = JsonDocument.Parse(output);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(doc.RootElement.GetProperty("summary").GetString(), Is.EqualTo("Whoami failed"));
    }

    [Test]
    public async Task SetRunnerCommand_WhenRunnerArgumentMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = SetRunnerCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Runner assignment failed", CommandDiagnosticCheckNames.RunnerLookup, "checks", "nextSteps");
    }

    [Test]
    public async Task NewCommand_WhenNameIsMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://automaton.monster",
            ApiKey = "ak_test"
        });
        var command = NewCommand.Build(config);

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "New failed", CommandDiagnosticCheckNames.CreateApp, "checks", "nextSteps");
    }

    [Test]
    public async Task CreateCommand_WhenUnlinkedProjectExists_UsesExistingProjectNameInJsonMode()
    {
        var projectRoot = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            Name = "Recovered App",
            Frontend = new FrontendConfig
            {
                DeployDir = "frontend/dist"
            }
        });
        config.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://automaton.monster",
            ApiKey = "ak_test"
        });

        var command = CreateCommand.Build(config);
        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("Recovered App"));
    }

    [Test]
    public async Task LinkCommand_WhenAppIdIsMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var config = new ConfigService();
        config.SaveProjectLocalConfig(projectRoot, new GlobalConfig
        {
            Server = "https://automaton.monster",
            ApiKey = "ak_test"
        });
        var command = LinkCommand.Build(config);

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Link failed", CommandDiagnosticCheckNames.LinkApp, "checks", "nextSteps");
    }

    [Test]
    public async Task SyncAiCommand_WhenInvokedWithJson_ReturnsFailureJsonShape_WhenNoServerBackedGuidanceIsAvailable()
    {
        var projectRoot = CreateProjectRoot();
        var command = SyncAiCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "AI guidance sync failed", CommandDiagnosticCheckNames.AiGuidance, "checks", "nextSteps");
    }

    [Test]
    public async Task SyncAiCommand_WhenAdapterIsRequested_ReturnsFailureJsonShape_WhenNoServerBackedGuidanceIsAvailable()
    {
        var projectRoot = CreateProjectRoot();
        var command = SyncAiCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--adapter", "claude", "--json"]);

        using var doc = JsonDocument.Parse(output);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
    }

    private static void AssertDiagnosticFailure(string output, string expectedSummary, string expectedCheckName, params string[] requiredTopLevelProperties)
    {
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("summary").GetString(), Is.EqualTo(expectedSummary));

        foreach (var propertyName in requiredTopLevelProperties)
        {
            Assert.That(root.TryGetProperty(propertyName, out _), Is.True, $"Expected property '{propertyName}' in output: {output}");
        }

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.That(checks.Any(check => string.Equals(check.GetProperty("name").GetString(), expectedCheckName, StringComparison.Ordinal)), Is.True, $"Expected check '{expectedCheckName}' in output: {output}");
    }

    private static async Task<string> InvokeAndCaptureAsync(Command command, string workingDirectory, string[] args, string? input = null)
    {
        var originalOut = Console.Out;
        var originalIn = Console.In;
        var writer = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
            Console.SetOut(writer);
            if (input != null)
            {
                Console.SetIn(new StringReader(input));
            }
            await command.InvokeAsync(args);
            return writer.ToString().Trim();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
        }
    }

    private string CreateProjectRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
