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
        new ConfigService().SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "App",
            Server = "https://api.example.com"
        });

        var command = StatusCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Status failed", CommandDiagnosticCheckNames.Credentials, "checks", "nextSteps");
    }

    [Test]
    public async Task RunnersCommand_WhenCredentialsAreMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = RunnersCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        AssertDiagnosticFailure(output, "Runner listing failed", CommandDiagnosticCheckNames.Credentials, "checks", "nextSteps");
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
    public async Task SetupCommand_WhenCredentialsAreMissing_ReturnsDiagnosticJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = SetupCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"], Environment.NewLine);

        AssertDiagnosticFailure(output, "Setup failed", CommandDiagnosticCheckNames.Credentials, "checks", "nextSteps");
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
    public async Task SyncAiCommand_WhenInvokedWithJson_ReturnsSuccessJsonShape()
    {
        var projectRoot = CreateProjectRoot();
        var command = SyncAiCommand.Build(new ConfigService());

        var output = await InvokeAndCaptureAsync(command, projectRoot, ["--json"]);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.That(root.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("summary").GetString(), Is.EqualTo("AI guidance sync complete"));
        Assert.That(root.GetProperty("mode").GetString(), Is.EqualTo(AiGuidanceModes.Project));
        Assert.That(root.GetProperty("manifestPath").GetString(), Does.Contain("manifest.json"));
        Assert.That(root.GetProperty("generatedFiles").GetArrayLength(), Is.GreaterThanOrEqualTo(10));

        var checks = root.GetProperty("checks").EnumerateArray().ToList();
        Assert.That(checks.Any(check => string.Equals(check.GetProperty("name").GetString(), CommandDiagnosticCheckNames.AiGuidance, StringComparison.Ordinal)), Is.True);
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