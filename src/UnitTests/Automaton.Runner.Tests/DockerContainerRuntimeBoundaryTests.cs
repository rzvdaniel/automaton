using Automaton.Runner.Console.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class DockerContainerRuntimeBoundaryTests
{
    [Test]
    public void HostPathTranslationUsesTheActualInfrastructureRunnerMountShape()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.That(
            translator.Translate("/automaton/workspace/apps/x/runtime"),
            Is.EqualTo("/var/lib/automaton/infrastructure-runner/workflows/apps/x/runtime"));
    }

    [Test]
    public void HostPathTranslationRejectsPathsOutsideTheManagedWorkspace()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.Throws<UnauthorizedAccessException>(() => translator.Translate("/etc/passwd"));
    }

    [Test]
    public void StructuredCommandUsesExplicitExecutableEntrypoint()
    {
        var args = new List<string>();
        var command = new ApplicationCommandSpec { Executable = "bin/server", Arguments = ["--port", "8080"] };

        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/app");
        DockerContainerRuntime.AddCommandArguments(args, command);

        Assert.That(args, Is.EqualTo(new[] { "--entrypoint", "/automaton/app/bin/server", "--port", "8080" }));
    }

    [Test]
    public void ShellCommandUsesShEntrypointAndSingleQuotedCommand()
    {
        var args = new List<string>();
        var command = new ApplicationCommandSpec
        {
            Executable = "server --mode 'safe'",
            Arguments = ["--flag"],
            UseShell = true
        };

        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/app");
        DockerContainerRuntime.AddCommandArguments(args, command);

        Assert.That(args, Is.EqualTo(new[] { "--entrypoint", "/bin/sh", "-c", @"'server --mode '\''safe'\''' '--flag'" }));
    }
}
