using System.Diagnostics;
using Automaton.Core.Models;
using Automaton.Core.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class ApplicationBuildExecutorTests
{
    private string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "automaton-build-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task ExecuteAsync_RunsArbitraryCommandWithStructuredInputsAndLogs()
    {
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = ["--version"],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "test" }
                }
            },
            root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.Canceled, Is.False);
            Assert.That(result.StandardOutput, Is.Not.Empty);
            Assert.That(result.Logs, Has.Some.StartsWith("stdout: "));
        });
    }

    [Test]
    public async Task ExecuteAsync_ReturnsDeterministicFailureAndLogs()
    {
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = ["--not-a-real-build-option"],
                    WorkingDirectory = "."
                }
            },
            root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StandardError, Is.Not.Empty);
            Assert.That(result.Logs, Has.Some.StartsWith("stderr: "));
        });
    }

    [Test]
    public async Task ExecuteAsync_PreservesUsefulStdoutDiagnosticsOnFailure()
    {
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), "--fail-with-output"],
                    WorkingDirectory = "."
                }
            },
            root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(3));
            Assert.That(result.StandardOutput, Does.Contain("useful stdout diagnostic"));
            Assert.That(result.Logs, Has.Some.EqualTo("stdout: useful stdout diagnostic"));
        });
    }

    [Test]
    public async Task ExecuteAsync_ReturnsFailureWhenExecutableCannotStart()
    {
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec
                {
                    Executable = "missing-build-executable",
                    WorkingDirectory = "."
                }
            },
            root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Logs, Has.Some.StartsWith("error: "));
        });
    }

    [Test]
    public async Task ExecuteAsync_TimesOutAndTerminatesTheBuildProcess()
    {
        var fixturePath = FixturePath();
        var port = 31_000 + Random.Shared.Next(500);
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                TimeoutSeconds = 1,
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [fixturePath, "--tcp"],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string>
                    {
                        ["AUTOMATON_PORT"] = port.ToString()
                    }
                }
            },
            root);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.Canceled, Is.False);
        });
    }

    [Test]
    public async Task ExecuteAsync_HonorsCancellationAndTerminatesTheBuildProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                TimeoutSeconds = 60,
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [FixturePath(), "--tcp"],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string>
                    {
                        ["AUTOMATON_PORT"] = (31_500 + Random.Shared.Next(500)).ToString()
                    }
                }
            },
            root,
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Canceled, Is.True);
            Assert.That(result.TimedOut, Is.False);
        });
    }

    [Test]
    public async Task ExecuteAsync_TerminatesBuildProcessTreeOnTimeout()
    {
        var fixturePath = FixturePath();
        var result = await ApplicationBuildExecutor.ExecuteAsync(
            new ApplicationBuildSpec
            {
                TimeoutSeconds = 1,
                Command = new ApplicationCommandSpec
                {
                    Executable = "dotnet",
                    Arguments = [fixturePath, "--spawn-child"],
                    WorkingDirectory = ".",
                    Environment = new Dictionary<string, string>
                    {
                        ["AUTOMATON_PORT"] = (32_000 + Random.Shared.Next(500)).ToString()
                    }
                }
            },
            root);

        var childPid = ParseChildPid(result.StandardOutput);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.TimedOut, Is.True);
            Assert.That(result.ProcessId, Is.Not.Null);
            Assert.That(IsProcessAlive(result.ProcessId!.Value), Is.False);
            Assert.That(IsProcessAlive(childPid), Is.False);
        });
    }

    [Test]
    public void ExecuteAsync_RejectsWorkingDirectoryOutsideWorkspace()
    {
        Assert.That(
            async () => await ApplicationBuildExecutor.ExecuteAsync(
                new ApplicationBuildSpec
                {
                    Command = new ApplicationCommandSpec
                    {
                        Executable = "dotnet",
                        Arguments = ["--version"],
                        WorkingDirectory = "../outside"
                    }
                },
                root),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static string FixturePath()
        => Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..",
            "RunnerRuntimeFixture", "bin", "Debug", "net9.0", "RunnerRuntimeFixture.dll"));

    private static int ParseChildPid(IEnumerable<string> output)
    {
        var line = output.Single(value => value.StartsWith("child:", StringComparison.Ordinal));
        return int.Parse(line["child:".Length..]);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
