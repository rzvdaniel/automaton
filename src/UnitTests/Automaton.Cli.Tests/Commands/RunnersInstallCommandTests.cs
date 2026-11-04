using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class RunnersInstallCommandTests
{
    [Test]
    public void SshInstallerReceivesSecretOnlyOnStdinAndRedactsFailure()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "automaton-cli-ssh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var script = Path.Combine(directory, "ssh");
            File.WriteAllText(script, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$TEST_SSH_ARGS\"\ncat > \"$TEST_SSH_STDIN\"\necho enrollment-secret >&2\nexit 7\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var argsPath = Path.Combine(directory, "args");
            var stdinPath = Path.Combine(directory, "stdin");
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + oldPath);
            Environment.SetEnvironmentVariable("TEST_SSH_ARGS", argsPath);
            Environment.SetEnvironmentVariable("TEST_SSH_STDIN", stdinPath);
            var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RunnersInstallCommand.ExecuteAsync("AUTOMATON_ENROLLMENT_TOKEN='enrollment-secret'", "user@host", CancellationToken.None));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(argsPath), Does.Contain("user@host"));
                Assert.That(File.ReadAllText(argsPath), Does.Not.Contain("enrollment-secret"));
                Assert.That(File.ReadAllText(stdinPath), Does.Contain("enrollment-secret"));
                Assert.That(error!.Message, Does.Contain("exited 7"));
                Assert.That(error.Message, Does.Not.Contain("enrollment-secret"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("TEST_SSH_ARGS", null);
            Environment.SetEnvironmentVariable("TEST_SSH_STDIN", null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("--name", "test", "--local", "--json", "--yes", "--timeout", "0")]
    [TestCase("--name", "test", "--local", "--ssh", "user@host", "--json", "--yes")]
    [TestCase("--name", "test", "--json", "--yes")]
    [TestCase("--name", "test", "--local", "--json")]
    public async Task InvalidOrUnconfirmedInvocationFailsBeforeCreatingInstallation(params string[] args)
    {
        var root = new RootCommand { RunnersCommand.Build(new ConfigService()) };
        var parser = new CommandLineBuilder(root).Build();
        using var output = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            var code = await parser.InvokeAsync(["runners", "install", .. args]);
            Assert.That(code, Is.EqualTo(1));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.That(json.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
        finally { Console.SetOut(previous); }
    }
}
